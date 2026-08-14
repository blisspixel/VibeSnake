using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public enum ScoreHistoryLoadCode : byte
{
    Success = 0,
    Empty = 1,
    InvalidJson = 2,
    UnsupportedSchema = 3,
    InvalidField = 4,
    TooLarge = 5,
    IoError = 6,
}

public sealed record ScoreHistoryLoadResult(
    ScoreHistoryLoadCode Code,
    string Message,
    ScoreHistoryDocument? Document = null)
{
    public bool IsSuccess => Code == ScoreHistoryLoadCode.Success && Document is not null;
}

public enum PythonScoreImportCode : byte
{
    Success = 0,
    AlreadyImported = 1,
    SourceNotFound = 2,
    SourceTooLarge = 3,
    InvalidSource = 4,
    DestinationBlocked = 5,
    IoError = 6,
}

public sealed record PythonScoreImportResult(
    PythonScoreImportCode Code,
    string Message,
    int ImportedEntryCount = 0,
    string? SourceSha256 = null,
    ScoreHistoryDocument? Document = null)
{
    public bool IsSuccess => Code is PythonScoreImportCode.Success
        or PythonScoreImportCode.AlreadyImported;
}

public sealed record ScoreHistoryEntry(
    long Sequence,
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
    int Score,
    string PlayerLabel,
    string RecordedAtUtc,
    string SourceId)
{
    public string CategoryKey =>
        $"{DisplayCategoryId}|{RunKindId}|{SeedCategoryId}|{RulesetId}@{RulesVersion}|"
        + $"{ModeId}@{ModeVersion}|{ScoreCategoryId}|{DifficultyPolicyId}|"
        + $"{AdaptivePolicyId}|{ConfigHashAlgorithm}|{ConfigHash}";

    public PersonalBestEntry ToPersonalBestEntry() => new(
        RulesetId,
        RulesVersion,
        ModeId,
        ModeVersion,
        RunKindId,
        SeedCategoryId,
        ScoreCategoryId,
        DifficultyPolicyId,
        AdaptationEnabled,
        AdaptivePolicyId,
        DisplayCategoryId,
        ConfigHash,
        ConfigHashAlgorithm,
        Score);
}

public sealed record ScoreHistoryUpdate(
    ScoreHistoryDocument Document,
    bool Retained,
    int? Rank);

public sealed record PersonalBestHistoryMerge(
    ScoreHistoryDocument Document,
    int AddedEntryCount);

/// <summary>
/// Bounded local top-ten history per exact fair-score category. The document
/// owns native terminal scores and a one-time, visibly noncompetitive import
/// of the Python alpha top ten.
/// </summary>
public sealed record ScoreHistoryDocument(
    int SchemaVersion,
    long NextSequence,
    bool PythonTopTenImported,
    string PythonTopTenSourceSha256,
    int PythonTopTenImportedCount,
    IReadOnlyList<ScoreHistoryEntry> Entries)
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "score_history.json";
    public const int MaximumScoresPerCategory = 10;
    public const int MaximumCategoryCount = PersonalBestDocument.MaximumEntryCount;
    public const int MaximumEntryCount = MaximumCategoryCount * MaximumScoresPerCategory;
    public const int MaximumPlayerLabelCharacters = 24;
    public const int MaximumTimestampCharacters = 64;
    public const int MaximumSourceCharacters = 64;
    public const long MaximumDocumentBytes = 1024L * 1024L;
    public const string NativeTerminalSourceId = "native-terminal";
    public const string PersonalBestMigrationSourceId = "native-personal-best-v2";
    public const string PythonTopTenSourceId = "python-high-scores-v1";
    public const string LocalPlayerLabel = "LOCAL PLAYER";
    public const string UnknownTimestamp = "unknown";
    public const string PythonLegacyRulesetId = "vibesnake-python-alpha";
    public const int PythonLegacyRulesVersion = 1;
    public const string PythonLegacyConfigHashAlgorithm = "sha256-legacy-unknown-v1";
    public const string PythonLegacyConfigHash =
        "a6fc3a24fd851acdb4146e619f80292c86ebcc76d332f6bc2c599c3a79e535da";

    public static ScoreHistoryDocument CreateDefaults() => new(
        CurrentSchemaVersion,
        NextSequence: 1,
        PythonTopTenImported: false,
        PythonTopTenSourceSha256: string.Empty,
        PythonTopTenImportedCount: 0,
        Entries: Array.Empty<ScoreHistoryEntry>());

    public ScoreHistoryUpdate Add(
        RunScoreIdentity identity,
        DateTimeOffset recordedAtUtc,
        string playerLabel = LocalPlayerLabel)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.Status is not (RunStatus.Dead or RunStatus.Won))
        {
            throw new ArgumentException(
                "Only terminal run results can enter score history.",
                nameof(identity));
        }

        if (!identity.CompetitiveEligible)
        {
            throw new ArgumentException(
                "Only competitive-eligible run kinds can enter native score history.",
                nameof(identity));
        }

        var entry = FromIdentity(
            identity,
            NextSequence,
            NormalizePlayerLabel(playerLabel),
            recordedAtUtc.ToUniversalTime().ToString("O"),
            NativeTerminalSourceId);
        return Insert(entry);
    }

    public PersonalBestHistoryMerge MergePersonalBests(PersonalBestDocument personalBests)
    {
        ArgumentNullException.ThrowIfNull(personalBests);
        _ = personalBests.SerializeCanonical();
        var document = this;
        var added = 0;
        foreach (var personalBest in personalBests.Entries)
        {
            if (document.Entries.Any(entry =>
                    entry.CategoryKey == personalBest.CategoryKey
                    && entry.Score == personalBest.BestScore))
            {
                continue;
            }

            var candidate = FromPersonalBest(personalBest, document.NextSequence);
            var update = document.Insert(candidate);
            document = update.Document;
            if (update.Retained)
            {
                added++;
            }
        }

        return new PersonalBestHistoryMerge(document, added);
    }

    internal ScoreHistoryDocument ImportPythonTopTen(
        IReadOnlyList<PythonScoreEntry> scores,
        string sourceSha256)
    {
        ArgumentNullException.ThrowIfNull(scores);
        if (PythonTopTenImported)
        {
            return this;
        }

        if (!IsLowerHexSha256(sourceSha256) || scores.Count > MaximumScoresPerCategory)
        {
            throw new InvalidDataException("Python top-ten import metadata is invalid.");
        }

        var retained = Entries
            .Where(entry => entry.CategoryKey != PythonImportCategoryKey)
            .ToList();
        var sequence = NextSequence;
        foreach (var score in scores
                     .OrderByDescending(item => item.Score)
                     .ThenBy(item => item.SourceOrder))
        {
            retained.Add(FromPythonScore(score, sequence));
            sequence++;
        }

        var document = new ScoreHistoryDocument(
            CurrentSchemaVersion,
            sequence,
            PythonTopTenImported: true,
            sourceSha256,
            scores.Count,
            NormalizeEntries(retained));
        Validate(document);
        return document;
    }

    public IReadOnlyList<ScoreHistoryEntry> ScoresForCategory(string categoryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryKey);
        return Entries
            .Where(entry => entry.CategoryKey == categoryKey)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Sequence)
            .ToArray();
    }

    public string SerializeCanonical()
    {
        Validate(this);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteNumber("nextSequence", NextSequence);
            writer.WriteBoolean("pythonTopTenImported", PythonTopTenImported);
            writer.WriteString("pythonTopTenSourceSha256", PythonTopTenSourceSha256);
            writer.WriteNumber("pythonTopTenImportedCount", PythonTopTenImportedCount);
            writer.WriteStartArray("entries");
            foreach (var entry in NormalizeEntries(Entries))
            {
                writer.WriteStartObject();
                writer.WriteNumber("sequence", entry.Sequence);
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
                writer.WriteNumber("score", entry.Score);
                writer.WriteString("playerLabel", entry.PlayerLabel);
                writer.WriteString("recordedAtUtc", entry.RecordedAtUtc);
                writer.WriteString("sourceId", entry.SourceId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    public static ScoreHistoryLoadResult Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ScoreHistoryLoadResult(
                ScoreHistoryLoadCode.Empty,
                "Score-history document is empty.");
        }

        if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
        {
            return new ScoreHistoryLoadResult(
                ScoreHistoryLoadCode.TooLarge,
                "Score-history document exceeds the byte limit.");
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new ScoreHistoryLoadResult(
                ScoreHistoryLoadCode.InvalidJson,
                "Score-history JSON is invalid: " + exception.Message);
        }

        using (parsed)
        {
            try
            {
                var root = parsed.RootElement;
                RequireObject(
                    root,
                    "score history",
                    [
                        "schemaVersion",
                        "nextSequence",
                        "pythonTopTenImported",
                        "pythonTopTenSourceSha256",
                        "pythonTopTenImportedCount",
                        "entries",
                    ]);
                var schemaVersion = ReadInt(root, "schemaVersion");
                if (schemaVersion != CurrentSchemaVersion)
                {
                    return new ScoreHistoryLoadResult(
                        ScoreHistoryLoadCode.UnsupportedSchema,
                        "Score-history schema is unsupported: " + schemaVersion + ".");
                }

                if (!root.TryGetProperty("entries", out var entriesElement)
                    || entriesElement.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("entries must be an array.");
                }

                var entries = entriesElement.EnumerateArray().Select(ReadEntry).ToArray();
                var document = new ScoreHistoryDocument(
                    schemaVersion,
                    ReadLong(root, "nextSequence"),
                    ReadBool(root, "pythonTopTenImported"),
                    ReadString(root, "pythonTopTenSourceSha256"),
                    ReadInt(root, "pythonTopTenImportedCount"),
                    entries);
                Validate(document);
                return new ScoreHistoryLoadResult(
                    ScoreHistoryLoadCode.Success,
                    "Score history loaded.",
                    document with { Entries = NormalizeEntries(entries) });
            }
            catch (InvalidDataException exception)
            {
                return new ScoreHistoryLoadResult(
                    ScoreHistoryLoadCode.InvalidField,
                    exception.Message);
            }
        }
    }

    private ScoreHistoryUpdate Insert(ScoreHistoryEntry entry)
    {
        Validate(this);
        ValidateEntry(entry);
        if (entry.Sequence != NextSequence)
        {
            throw new InvalidDataException("New score sequence does not match nextSequence.");
        }

        var categoryExists = Entries.Any(existing => existing.CategoryKey == entry.CategoryKey);
        if (!categoryExists
            && Entries.Select(existing => existing.CategoryKey).Distinct(StringComparer.Ordinal).Count()
                >= MaximumCategoryCount)
        {
            throw new InvalidOperationException("Score-history category capacity is exhausted.");
        }

        var category = Entries
            .Where(existing => existing.CategoryKey == entry.CategoryKey)
            .Append(entry)
            .OrderByDescending(existing => existing.Score)
            .ThenBy(existing => existing.Sequence)
            .Take(MaximumScoresPerCategory)
            .ToArray();
        var retained = category.Any(existing => existing.Sequence == entry.Sequence);
        if (!retained)
        {
            return new ScoreHistoryUpdate(this, Retained: false, Rank: null);
        }

        var combined = Entries
            .Where(existing => existing.CategoryKey != entry.CategoryKey)
            .Concat(category)
            .ToArray();
        var document = this with
        {
            NextSequence = NextSequence + 1,
            Entries = NormalizeEntries(combined),
        };
        Validate(document);
        var rank = category
            .Select((candidate, index) => (candidate, rank: index + 1))
            .Single(item => item.candidate.Sequence == entry.Sequence).rank;
        return new ScoreHistoryUpdate(document, Retained: true, rank);
    }

    private static ScoreHistoryEntry FromIdentity(
        RunScoreIdentity identity,
        long sequence,
        string playerLabel,
        string recordedAtUtc,
        string sourceId) => new(
            sequence,
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
            identity.Score,
            playerLabel,
            recordedAtUtc,
            sourceId);

    private static ScoreHistoryEntry FromPersonalBest(
        PersonalBestEntry personalBest,
        long sequence) => new(
            sequence,
            personalBest.RulesetId,
            personalBest.RulesVersion,
            personalBest.ModeId,
            personalBest.ModeVersion,
            personalBest.RunKindId,
            personalBest.SeedCategoryId,
            personalBest.ScoreCategoryId,
            personalBest.DifficultyPolicyId,
            personalBest.AdaptationEnabled,
            personalBest.AdaptivePolicyId,
            personalBest.DisplayCategoryId,
            personalBest.ConfigHash,
            personalBest.ConfigHashAlgorithm,
            personalBest.BestScore,
            LocalPlayerLabel,
            UnknownTimestamp,
            PersonalBestMigrationSourceId);

    private static string PythonImportCategoryKey => FromPythonScore(
        new PythonScoreEntry(0, LocalPlayerLabel, 1, UnknownTimestamp),
        sequence: 0).CategoryKey;

    private static ScoreHistoryEntry FromPythonScore(PythonScoreEntry score, long sequence) => new(
        sequence,
        PythonLegacyRulesetId,
        PythonLegacyRulesVersion,
        PersonalBestDocument.LegacyModeId,
        1,
        ScoreRunContextCatalog.LegacyRunKind,
        ScoreRunContextCatalog.LegacySeedCategory,
        PersonalBestDocument.LegacyScoreCategoryId,
        PersonalBestDocument.LegacyDifficultyPolicyId,
        AdaptationEnabled: false,
        AdaptiveDifficultyPolicy.DisabledPolicyId,
        ScoreRunContextCatalog.LegacyDisplayCategory,
        PythonLegacyConfigHash,
        PythonLegacyConfigHashAlgorithm,
        score.Score,
        score.PlayerLabel,
        score.RecordedAt,
        PythonTopTenSourceId);

    private static ScoreHistoryEntry[] NormalizeEntries(
        IEnumerable<ScoreHistoryEntry> entries) => entries
        .OrderBy(entry => entry.CategoryKey, StringComparer.Ordinal)
        .ThenByDescending(entry => entry.Score)
        .ThenBy(entry => entry.Sequence)
        .ToArray();

    private static void Validate(ScoreHistoryDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion
            || document.NextSequence < 1
            || document.Entries.Count > MaximumEntryCount)
        {
            throw new InvalidDataException("Score-history document is not canonical.");
        }

        if (document.PythonTopTenImported)
        {
            if (!IsLowerHexSha256(document.PythonTopTenSourceSha256)
                || document.PythonTopTenImportedCount < 0
                || document.PythonTopTenImportedCount > MaximumScoresPerCategory)
            {
                throw new InvalidDataException("Python top-ten import marker is invalid.");
            }
        }
        else if (document.PythonTopTenSourceSha256.Length != 0
                 || document.PythonTopTenImportedCount != 0)
        {
            throw new InvalidDataException("Incomplete Python import metadata is invalid.");
        }

        var sequences = new HashSet<long>();
        foreach (var entry in document.Entries)
        {
            ValidateEntry(entry);
            if (entry.Sequence >= document.NextSequence || !sequences.Add(entry.Sequence))
            {
                throw new InvalidDataException("Score-history sequences must be unique and bounded.");
            }
        }

        var categories = document.Entries.GroupBy(entry => entry.CategoryKey).ToArray();
        if (categories.Length > MaximumCategoryCount
            || categories.Any(group => group.Count() > MaximumScoresPerCategory))
        {
            throw new InvalidDataException("Score-history category bounds were exceeded.");
        }

        var importedCount = document.Entries.Count(entry =>
            entry.SourceId == PythonTopTenSourceId);
        if (document.PythonTopTenImported && importedCount != document.PythonTopTenImportedCount)
        {
            throw new InvalidDataException("Python import marker does not match retained entries.");
        }
    }

    private static void ValidateEntry(ScoreHistoryEntry entry)
    {
        if (entry.Sequence < 1
            || string.IsNullOrWhiteSpace(entry.PlayerLabel)
            || entry.PlayerLabel.Length > MaximumPlayerLabelCharacters
            || entry.PlayerLabel.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(entry.RecordedAtUtc)
            || entry.RecordedAtUtc.Length > MaximumTimestampCharacters
            || entry.RecordedAtUtc.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(entry.SourceId)
            || entry.SourceId.Length > MaximumSourceCharacters
            || entry.SourceId.Any(char.IsControl)
            || entry.SourceId is not (NativeTerminalSourceId
                or PersonalBestMigrationSourceId
                or PythonTopTenSourceId))
        {
            throw new InvalidDataException("Score-history display metadata is invalid.");
        }

        PersonalBestDocument.ValidateEntry(entry.ToPersonalBestEntry());
        var isLegacy = entry.RunKindId == ScoreRunContextCatalog.LegacyRunKind;
        if ((entry.SourceId == PythonTopTenSourceId && !isLegacy)
            || (entry.SourceId == NativeTerminalSourceId && isLegacy))
        {
            throw new InvalidDataException("Score-history source conflicts with score identity.");
        }
    }

    internal static string NormalizePlayerLabel(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = new string(value
            .Trim()
            .Select(character => char.IsControl(character) ? ' ' : character)
            .ToArray());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "Anonymous";
        }

        if (normalized.Length <= MaximumPlayerLabelCharacters)
        {
            return normalized;
        }

        var boundary = MaximumPlayerLabelCharacters;
        if (char.IsHighSurrogate(normalized[boundary - 1])
            && char.IsLowSurrogate(normalized[boundary]))
        {
            boundary--;
        }

        return normalized[..boundary];
    }

    private static bool IsLowerHexSha256(string value) => value.Length == 64
        && value.All(character => char.IsAsciiHexDigit(character) && !char.IsUpper(character));

    private static ScoreHistoryEntry ReadEntry(JsonElement element)
    {
        RequireObject(
            element,
            "score entry",
            [
                "sequence", "rulesetId", "rulesVersion", "modeId", "modeVersion",
                "runKindId", "seedCategoryId", "scoreCategoryId", "difficultyPolicyId",
                "adaptationEnabled", "adaptivePolicyId", "displayCategoryId", "configHash",
                "configHashAlgorithm", "score", "playerLabel", "recordedAtUtc", "sourceId",
            ]);
        return new ScoreHistoryEntry(
            ReadLong(element, "sequence"),
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
            ReadInt(element, "score"),
            ReadString(element, "playerLabel"),
            ReadString(element, "recordedAtUtc"),
            ReadString(element, "sourceId"));
    }

    private static void RequireObject(
        JsonElement element,
        string name,
        IReadOnlyCollection<string> allowedProperties)
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

        if (seen.Count != allowedProperties.Count)
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

    private static long ReadLong(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var value))
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

internal sealed record PythonScoreEntry(
    int SourceOrder,
    string PlayerLabel,
    int Score,
    string RecordedAt);

public sealed class ScoreHistoryStore
{
    public const string ImportDirectoryName = "imports";
    public const string PythonTopTenFileName = "high_scores.json";
    public const long MaximumPythonSourceBytes = 64L * 1024L;

    public ScoreHistoryStore(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        ScoreHistoryPath = Path.Combine(UserDataRoot, ScoreHistoryDocument.FileName);
        PythonImportInboxPath = Path.Combine(
            UserDataRoot,
            ImportDirectoryName,
            PythonTopTenFileName);
    }

    public string UserDataRoot { get; }

    public string ScoreHistoryPath { get; }

    public string PythonImportInboxPath { get; }

    public ScoreHistoryLoadResult Load()
    {
        if (!File.Exists(ScoreHistoryPath))
        {
            return new ScoreHistoryLoadResult(
                ScoreHistoryLoadCode.Success,
                "Score-history defaults applied.",
                ScoreHistoryDocument.CreateDefaults());
        }

        try
        {
            var info = new FileInfo(ScoreHistoryPath);
            if (info.Length > ScoreHistoryDocument.MaximumDocumentBytes)
            {
                return new ScoreHistoryLoadResult(
                    ScoreHistoryLoadCode.TooLarge,
                    "Score-history document exceeds the byte limit.");
            }

            return ScoreHistoryDocument.Read(File.ReadAllText(ScoreHistoryPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ScoreHistoryLoadResult(
                ScoreHistoryLoadCode.IoError,
                "Score-history file could not be read: " + exception.Message);
        }
    }

    public void Save(ScoreHistoryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var canonical = document.SerializeCanonical();
        if (Encoding.UTF8.GetByteCount(canonical) > ScoreHistoryDocument.MaximumDocumentBytes)
        {
            throw new InvalidDataException("Score-history document exceeds the byte limit.");
        }

        Directory.CreateDirectory(UserDataRoot);
        var temporaryPath = ScoreHistoryPath + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                canonical,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, ScoreHistoryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public string EnsurePythonImportInbox()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PythonImportInboxPath)!);
        return PythonImportInboxPath;
    }

    public PythonScoreImportResult ImportPythonTopTen()
    {
        var loaded = Load();
        if (!loaded.IsSuccess || loaded.Document is null)
        {
            return new PythonScoreImportResult(
                PythonScoreImportCode.DestinationBlocked,
                "Import blocked because current native score history is not writable: "
                    + loaded.Message);
        }

        if (loaded.Document.PythonTopTenImported)
        {
            return new PythonScoreImportResult(
                PythonScoreImportCode.AlreadyImported,
                "Python top ten was already imported; the source remains unchanged.",
                loaded.Document.PythonTopTenImportedCount,
                loaded.Document.PythonTopTenSourceSha256,
                loaded.Document);
        }

        EnsurePythonImportInbox();
        if (!File.Exists(PythonImportInboxPath))
        {
            return new PythonScoreImportResult(
                PythonScoreImportCode.SourceNotFound,
                "No Python high_scores.json was found in the import inbox.");
        }

        try
        {
            var info = new FileInfo(PythonImportInboxPath);
            if (info.Length > MaximumPythonSourceBytes)
            {
                return new PythonScoreImportResult(
                    PythonScoreImportCode.SourceTooLarge,
                    "Python high_scores.json exceeds the 64 KiB import limit.");
            }

            var bytes = File.ReadAllBytes(PythonImportInboxPath);
            if (bytes.LongLength > MaximumPythonSourceBytes)
            {
                return new PythonScoreImportResult(
                    PythonScoreImportCode.SourceTooLarge,
                    "Python high_scores.json changed beyond the 64 KiB import limit.");
            }

            var parsed = ParsePythonTopTen(bytes);
            var sourceHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var imported = loaded.Document.ImportPythonTopTen(parsed, sourceHash);
            Save(imported);
            return new PythonScoreImportResult(
                PythonScoreImportCode.Success,
                "Imported Python scores into visible Legacy 0.2 history; source unchanged.",
                parsed.Count,
                sourceHash,
                imported);
        }
        catch (InvalidDataException exception)
        {
            return new PythonScoreImportResult(
                PythonScoreImportCode.InvalidSource,
                exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PythonScoreImportResult(
                PythonScoreImportCode.IoError,
                "Python score import failed without changing the source: " + exception.Message);
        }
    }

    private static List<PythonScoreEntry> ParsePythonTopTen(byte[] bytes)
    {
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Python high_scores.json is invalid JSON.", exception);
        }

        using (parsed)
        {
            var root = parsed.RootElement;
            RequireExactObject(root, "Python score root", ["schema_version", "migrations", "scores"]);
            if (!root.TryGetProperty("schema_version", out var schema)
                || schema.ValueKind != JsonValueKind.Number
                || !schema.TryGetInt32(out var schemaVersion)
                || schemaVersion != 1)
            {
                throw new InvalidDataException("Python score schema must be exactly 1.");
            }

            if (!root.TryGetProperty("migrations", out var migrations))
            {
                throw new InvalidDataException("Python score migrations are missing.");
            }

            RequireExactObject(migrations, "Python score migrations", ["legacy_highscore_json"]);
            if (!migrations.TryGetProperty("legacy_highscore_json", out var migrated)
                || migrated.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidDataException("Python legacy migration marker must be boolean.");
            }

            if (!root.TryGetProperty("scores", out var scores)
                || scores.ValueKind != JsonValueKind.Array
                || scores.GetArrayLength() > ScoreHistoryDocument.MaximumScoresPerCategory)
            {
                throw new InvalidDataException("Python scores must be an array with at most ten rows.");
            }

            var result = new List<PythonScoreEntry>();
            var sourceOrder = 0;
            foreach (var score in scores.EnumerateArray())
            {
                RequireExactObject(score, "Python score entry", ["name", "score", "timestamp"]);
                if (!score.TryGetProperty("name", out var name)
                    || name.ValueKind != JsonValueKind.String
                    || !score.TryGetProperty("score", out var points)
                    || points.ValueKind != JsonValueKind.Number
                    || !points.TryGetInt32(out var scoreValue)
                    || scoreValue < 0
                    || scoreValue > SnakeRun.MaximumScore
                    || !score.TryGetProperty("timestamp", out var timestamp)
                    || timestamp.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("Python score entry contains an invalid field.");
                }

                var recordedAt = timestamp.GetString()!;
                if (string.IsNullOrWhiteSpace(recordedAt)
                    || recordedAt.Length > ScoreHistoryDocument.MaximumTimestampCharacters
                    || recordedAt.Any(char.IsControl))
                {
                    throw new InvalidDataException("Python score timestamp is invalid.");
                }

                result.Add(new PythonScoreEntry(
                    sourceOrder,
                    ScoreHistoryDocument.NormalizePlayerLabel(name.GetString()!),
                    scoreValue,
                    recordedAt));
                sourceOrder++;
            }

            return result;
        }
    }

    private static void RequireExactObject(
        JsonElement element,
        string name,
        IReadOnlyCollection<string> fields)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(name + " must be an object.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!fields.Contains(property.Name) || !seen.Add(property.Name))
            {
                throw new InvalidDataException(name + " has an unknown or duplicate field.");
            }
        }

        if (seen.Count != fields.Count)
        {
            throw new InvalidDataException(name + " is missing a required field.");
        }
    }
}
