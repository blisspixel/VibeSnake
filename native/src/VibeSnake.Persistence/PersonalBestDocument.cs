using System.Buffers;
using System.Text;
using System.Text.Json;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public enum PersonalBestLoadCode : byte
{
    Success = 0,
    Empty = 1,
    InvalidJson = 2,
    UnsupportedSchema = 3,
    InvalidField = 4,
    IoError = 5,
}

public sealed record PersonalBestLoadResult(
    PersonalBestLoadCode Code,
    string Message,
    PersonalBestDocument? Document = null)
{
    public bool IsSuccess => Code == PersonalBestLoadCode.Success && Document is not null;
}

public sealed record PersonalBestEntry(
    string RulesetId,
    int RulesVersion,
    string ModeId,
    int ModeVersion,
    string RunKindId,
    string SeedCategoryId,
    string ScoreCategoryId,
    string DifficultyPolicyId,
    bool AdaptationEnabled,
    string AdaptivePolicyId,
    string DisplayCategoryId,
    string ConfigHash,
    string ConfigHashAlgorithm,
    int BestScore)
{
    public string CategoryKey =>
        $"{DisplayCategoryId}|{RunKindId}|{SeedCategoryId}|{RulesetId}@{RulesVersion}|"
        + $"{ModeId}@{ModeVersion}|{ScoreCategoryId}|{DifficultyPolicyId}|"
        + $"{AdaptivePolicyId}|{ConfigHashAlgorithm}|{ConfigHash}";

    public bool Matches(RunScoreIdentity identity) =>
        string.Equals(RulesetId, identity.RulesetId, StringComparison.Ordinal)
        && RulesVersion == identity.RulesVersion
        && string.Equals(ModeId, identity.ModeId, StringComparison.Ordinal)
        && ModeVersion == identity.ModeVersion
        && string.Equals(RunKindId, identity.RunKindId, StringComparison.Ordinal)
        && string.Equals(SeedCategoryId, identity.SeedCategoryId, StringComparison.Ordinal)
        && string.Equals(ScoreCategoryId, identity.ScoreCategoryId, StringComparison.Ordinal)
        && string.Equals(
            DifficultyPolicyId,
            identity.DifficultyPolicyId,
            StringComparison.Ordinal)
        && AdaptationEnabled == identity.AdaptationEnabled
        && string.Equals(
            AdaptivePolicyId,
            identity.AdaptivePolicyId,
            StringComparison.Ordinal)
        && string.Equals(DisplayCategoryId, identity.DisplayCategoryId, StringComparison.Ordinal)
        && string.Equals(ConfigHash, identity.ConfigHash, StringComparison.Ordinal)
        && string.Equals(
            ConfigHashAlgorithm,
            identity.ConfigHashAlgorithm,
            StringComparison.Ordinal);
}

public sealed record PersonalBestUpdate(
    PersonalBestDocument Document,
    bool IsNewRecord,
    int? PreviousBestScore,
    int BestScore);

/// <summary>
/// Bounded local personal bests separated by the same rules and config identity
/// used by replays. Full ranked leaderboards remain a later progression layer.
/// </summary>
public sealed record PersonalBestDocument(
    int SchemaVersion,
    IReadOnlyList<PersonalBestEntry> Entries)
{
    public const int CurrentSchemaVersion = 2;
    public const string FileName = "personal_bests.json";
    public const int MaximumEntryCount = 64;
    public const int MaximumIdentityCharacters = 128;
    public const string LegacyModeId = "legacy-0.2";
    public const string LegacyScoreCategoryId = "legacy-0.2";
    public const string LegacyDifficultyPolicyId = "legacy-unknown";

    public static PersonalBestDocument CreateDefaults() =>
        new(CurrentSchemaVersion, Array.Empty<PersonalBestEntry>());

    public PersonalBestUpdate Apply(RunScoreIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.Status is not (RunStatus.Dead or RunStatus.Won))
        {
            throw new ArgumentException(
                "Only terminal run results can update a personal best.",
                nameof(identity));
        }

        if (!identity.CompetitiveEligible)
        {
            throw new ArgumentException(
                "Only competitive-eligible run kinds can update a personal best.",
                nameof(identity));
        }

        ValidateIdentity(identity);
        var entries = Entries.ToList();
        var index = entries.FindIndex(entry => entry.Matches(identity));
        int? previous = null;
        var isNewRecord = false;
        if (index >= 0)
        {
            previous = entries[index].BestScore;
            if (identity.Score > previous.Value)
            {
                entries[index] = entries[index] with { BestScore = identity.Score };
                isNewRecord = true;
            }
        }
        else
        {
            if (entries.Count >= MaximumEntryCount)
            {
                throw new InvalidOperationException(
                    "Personal-best category capacity is exhausted.");
            }

            entries.Add(
                new PersonalBestEntry(
                    identity.RulesetId,
                    identity.RulesVersion,
                    identity.ModeId,
                    identity.ModeVersion,
                    identity.RunKindId,
                    identity.SeedCategoryId,
                    identity.ScoreCategoryId,
                    identity.DifficultyPolicyId,
                    identity.AdaptationEnabled,
                    identity.AdaptivePolicyId,
                    identity.DisplayCategoryId,
                    identity.ConfigHash,
                    identity.ConfigHashAlgorithm,
                    identity.Score));
            isNewRecord = identity.Score > 0;
        }

        var normalized = new PersonalBestDocument(
            CurrentSchemaVersion,
            entries.OrderBy(entry => entry.CategoryKey, StringComparer.Ordinal).ToArray());
        return new PersonalBestUpdate(
            normalized,
            isNewRecord,
            previous,
            index >= 0 ? Math.Max(previous!.Value, identity.Score) : identity.Score);
    }

    public PersonalBestEntry? Find(RunScoreIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Entries.FirstOrDefault(entry => entry.Matches(identity));
    }

    public string SerializeCanonical()
    {
        Validate(this);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteStartArray("entries");
            foreach (var entry in Entries.OrderBy(item => item.CategoryKey, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("rulesetId", entry.RulesetId);
                writer.WriteNumber("rulesVersion", entry.RulesVersion);
                writer.WriteString("modeId", entry.ModeId);
                writer.WriteNumber("modeVersion", entry.ModeVersion);
                writer.WriteString("runKindId", entry.RunKindId);
                writer.WriteString("seedCategoryId", entry.SeedCategoryId);
                writer.WriteString("scoreCategoryId", entry.ScoreCategoryId);
                writer.WriteString("difficultyPolicyId", entry.DifficultyPolicyId);
                writer.WriteBoolean("adaptationEnabled", entry.AdaptationEnabled);
                writer.WriteString("adaptivePolicyId", entry.AdaptivePolicyId);
                writer.WriteString("displayCategoryId", entry.DisplayCategoryId);
                writer.WriteString("configHash", entry.ConfigHash);
                writer.WriteString("configHashAlgorithm", entry.ConfigHashAlgorithm);
                writer.WriteNumber("bestScore", entry.BestScore);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    public static PersonalBestLoadResult Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PersonalBestLoadResult(
                PersonalBestLoadCode.Empty,
                "Personal-best document is empty.");
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new PersonalBestLoadResult(
                PersonalBestLoadCode.InvalidJson,
                "Personal-best JSON is invalid: " + exception.Message);
        }

        using (parsed)
        {
            try
            {
                var root = parsed.RootElement;
                RequireObject(root, "root", ["schemaVersion", "entries"]);
                var schemaVersion = ReadInt(root, "schemaVersion");
                if (schemaVersion is < 1 or > CurrentSchemaVersion)
                {
                    return new PersonalBestLoadResult(
                        PersonalBestLoadCode.UnsupportedSchema,
                        $"Personal-best schemaVersion {schemaVersion} is unsupported.");
                }

                if (!root.TryGetProperty("entries", out var entriesElement)
                    || entriesElement.ValueKind != JsonValueKind.Array
                    || entriesElement.GetArrayLength() > MaximumEntryCount)
                {
                    throw new InvalidDataException(
                        $"entries must be an array with at most {MaximumEntryCount} items.");
                }

                var entries = new List<PersonalBestEntry>();
                var categories = new HashSet<string>(StringComparer.Ordinal);
                foreach (var element in entriesElement.EnumerateArray())
                {
                    var entry = schemaVersion == 1
                        ? ReadLegacyEntry(element)
                        : ReadCurrentEntry(element);
                    ValidateEntry(entry);
                    if (!categories.Add(entry.CategoryKey))
                    {
                        throw new InvalidDataException(
                            "Personal-best categories must be unique.");
                    }

                    entries.Add(entry);
                }

                return new PersonalBestLoadResult(
                    PersonalBestLoadCode.Success,
                    schemaVersion == CurrentSchemaVersion
                        ? "Personal-best document loaded."
                        : "Personal-best schema 1 migrated into visible Legacy 0.2 categories.",
                    new PersonalBestDocument(
                        CurrentSchemaVersion,
                        entries.OrderBy(entry => entry.CategoryKey, StringComparer.Ordinal).ToArray()));
            }
            catch (InvalidDataException exception)
            {
                return new PersonalBestLoadResult(
                    PersonalBestLoadCode.InvalidField,
                    exception.Message);
            }
        }
    }

    private static void Validate(PersonalBestDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion
            || document.Entries.Count > MaximumEntryCount)
        {
            throw new InvalidDataException("Personal-best document is not canonical.");
        }

        var categories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.Entries)
        {
            ValidateEntry(entry);
            if (!categories.Add(entry.CategoryKey))
            {
                throw new InvalidDataException("Personal-best categories must be unique.");
            }
        }
    }

    private static void ValidateIdentity(RunScoreIdentity identity) => ValidateEntry(
        new PersonalBestEntry(
            identity.RulesetId,
            identity.RulesVersion,
            identity.ModeId,
            identity.ModeVersion,
            identity.RunKindId,
            identity.SeedCategoryId,
            identity.ScoreCategoryId,
            identity.DifficultyPolicyId,
            identity.AdaptationEnabled,
            identity.AdaptivePolicyId,
            identity.DisplayCategoryId,
            identity.ConfigHash,
            identity.ConfigHashAlgorithm,
            identity.Score));

    internal static void ValidateEntry(PersonalBestEntry entry)
    {
        var isLegacy = entry.ModeId == LegacyModeId;
        if (string.IsNullOrWhiteSpace(entry.RulesetId)
            || entry.RulesetId.Length > MaximumIdentityCharacters
            || entry.RulesVersion <= 0
            || string.IsNullOrWhiteSpace(entry.ConfigHashAlgorithm)
            || entry.ConfigHashAlgorithm.Length > MaximumIdentityCharacters
            || entry.ConfigHash.Length != 64
            || entry.ConfigHash.Any(character =>
                !char.IsAsciiHexDigit(character) || char.IsUpper(character))
            || entry.BestScore < 0
            || entry.BestScore > SnakeRun.MaximumScore)
        {
            throw new InvalidDataException("Personal-best entry contains an invalid field.");
        }

        if (isLegacy)
        {
            if (entry.ModeVersion != 1
                || entry.RunKindId != ScoreRunContextCatalog.LegacyRunKind
                || entry.SeedCategoryId != ScoreRunContextCatalog.LegacySeedCategory
                || entry.ScoreCategoryId != LegacyScoreCategoryId
                || entry.DifficultyPolicyId != LegacyDifficultyPolicyId
                || entry.AdaptationEnabled
                || entry.AdaptivePolicyId != AdaptiveDifficultyPolicy.DisabledPolicyId
                || entry.DisplayCategoryId != ScoreRunContextCatalog.LegacyDisplayCategory)
            {
                throw new InvalidDataException("Legacy personal-best identity is invalid.");
            }

            return;
        }

        ScoreRunContext context;
        RunModeDefinition mode;
        try
        {
            context = ScoreRunContextCatalog.Get(entry.RunKindId, entry.SeedCategoryId);
            mode = RunModeCatalog.Get(entry.ModeId, entry.ModeVersion);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Personal-best category identity is invalid.", exception);
        }

        var expectedScoreCategory = entry.ModeId switch
        {
            RunModeCatalog.ClassicId => RunModeCatalog.ClassicScoreCategoryId,
            RunModeCatalog.VibeId when entry.AdaptationEnabled =>
                RunModeCatalog.VibeAdaptiveScoreCategoryId,
            RunModeCatalog.VibeId => RunModeCatalog.VibeFixedScoreCategoryId,
            _ => throw new InvalidDataException("Personal-best mode is invalid."),
        };
        var expectedAdaptivePolicy = entry.AdaptationEnabled
            ? AdaptiveDifficultyPolicy.CurrentPolicyId
            : AdaptiveDifficultyPolicy.DisabledPolicyId;
        if (!context.CompetitiveEligible
            || entry.DisplayCategoryId != context.DisplayCategoryId
            || entry.ScoreCategoryId != expectedScoreCategory
            || entry.DifficultyPolicyId != mode.DifficultyPolicyId
            || entry.AdaptivePolicyId != expectedAdaptivePolicy
            || (entry.ModeId == RunModeCatalog.ClassicId && entry.AdaptationEnabled))
        {
            throw new InvalidDataException("Personal-best mode or run category conflicts.");
        }
    }

    private static PersonalBestEntry ReadLegacyEntry(JsonElement element)
    {
        RequireObject(
            element,
            "entry",
            [
                "rulesetId",
                "rulesVersion",
                "configHash",
                "configHashAlgorithm",
                "bestScore",
            ]);
        return new PersonalBestEntry(
            ReadString(element, "rulesetId"),
            ReadInt(element, "rulesVersion"),
            LegacyModeId,
            1,
            ScoreRunContextCatalog.LegacyRunKind,
            ScoreRunContextCatalog.LegacySeedCategory,
            LegacyScoreCategoryId,
            LegacyDifficultyPolicyId,
            AdaptationEnabled: false,
            AdaptiveDifficultyPolicy.DisabledPolicyId,
            ScoreRunContextCatalog.LegacyDisplayCategory,
            ReadString(element, "configHash"),
            ReadString(element, "configHashAlgorithm"),
            ReadInt(element, "bestScore"));
    }

    private static PersonalBestEntry ReadCurrentEntry(JsonElement element)
    {
        RequireObject(
            element,
            "entry",
            [
                "rulesetId",
                "rulesVersion",
                "modeId",
                "modeVersion",
                "runKindId",
                "seedCategoryId",
                "scoreCategoryId",
                "difficultyPolicyId",
                "adaptationEnabled",
                "adaptivePolicyId",
                "displayCategoryId",
                "configHash",
                "configHashAlgorithm",
                "bestScore",
            ]);
        return new PersonalBestEntry(
            ReadString(element, "rulesetId"),
            ReadInt(element, "rulesVersion"),
            ReadString(element, "modeId"),
            ReadInt(element, "modeVersion"),
            ReadString(element, "runKindId"),
            ReadString(element, "seedCategoryId"),
            ReadString(element, "scoreCategoryId"),
            ReadString(element, "difficultyPolicyId"),
            ReadBool(element, "adaptationEnabled"),
            ReadString(element, "adaptivePolicyId"),
            ReadString(element, "displayCategoryId"),
            ReadString(element, "configHash"),
            ReadString(element, "configHashAlgorithm"),
            ReadInt(element, "bestScore"));
    }

    private static void RequireObject(
        JsonElement element,
        string name,
        string[] allowedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(name + " must be an object.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name) || !seen.Add(property.Name))
            {
                throw new InvalidDataException(
                    name + " contains an unknown or duplicate field: " + property.Name);
            }
        }

        if (seen.Count != allowedProperties.Length)
        {
            throw new InvalidDataException(name + " is missing a required field.");
        }
    }

    private static string ReadString(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var element)
            || element.ValueKind != JsonValueKind.String
            || element.GetString() is not { } value)
        {
            throw new InvalidDataException(field + " must be a string.");
        }

        return value;
    }

    private static int ReadInt(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var value))
        {
            throw new InvalidDataException(field + " must be an integer.");
        }

        return value;
    }

    private static bool ReadBool(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var element)
            || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(field + " must be a boolean.");
        }

        return element.GetBoolean();
    }
}

public sealed class PersonalBestStore
{
    public PersonalBestStore(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        PersonalBestPath = Path.Combine(UserDataRoot, PersonalBestDocument.FileName);
    }

    public string UserDataRoot { get; }

    public string PersonalBestPath { get; }

    public PersonalBestLoadResult Load()
    {
        if (!File.Exists(PersonalBestPath))
        {
            return new PersonalBestLoadResult(
                PersonalBestLoadCode.Success,
                "Personal-best defaults applied.",
                PersonalBestDocument.CreateDefaults());
        }

        try
        {
            return PersonalBestDocument.Read(File.ReadAllText(PersonalBestPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PersonalBestLoadResult(
                PersonalBestLoadCode.IoError,
                "Personal-best file could not be read: " + exception.Message);
        }
    }

    public void Save(PersonalBestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(UserDataRoot);
        var temporaryPath = PersonalBestPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            document.SerializeCanonical(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, PersonalBestPath, overwrite: true);
    }
}
