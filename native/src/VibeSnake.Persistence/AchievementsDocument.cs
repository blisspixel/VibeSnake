using System.Buffers;
using System.Text;
using System.Text.Json;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

/// <summary>
/// Load outcome for a versioned achievements document.
/// </summary>
public enum AchievementsLoadCode : byte
{
    Success = 0,
    Empty = 1,
    InvalidJson = 2,
    UnsupportedSchema = 3,
    InvalidField = 4,
    IoError = 5,
}

/// <summary>
/// Result of reading an achievements document from untrusted JSON.
/// </summary>
public sealed record AchievementsLoadResult(
    AchievementsLoadCode Code,
    string Message,
    AchievementsDocument? Document = null)
{
    public bool IsSuccess => Code == AchievementsLoadCode.Success && Document is not null;
}

/// <summary>
/// Schema 1 profile unlock document. Stores only catalog IDs that have been
/// permanently unlocked. Run-local candidate emission remains rules-owned;
/// shells call <see cref="WithUnlocks"/> after terminal candidates fire.
/// </summary>
public sealed record AchievementsDocument(
    int SchemaVersion,
    IReadOnlyList<string> UnlockedIds)
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "achievements.json";
    public const int MaximumUnlockCount = 256;

    public static AchievementsDocument CreateDefaults() =>
        new(SchemaVersion: CurrentSchemaVersion, UnlockedIds: Array.Empty<string>());

    /// <summary>
    /// Count of permanently unlocked catalog IDs.
    /// </summary>
    public int UnlockedCount => UnlockedIds.Count;

    /// <summary>
    /// Ordered unique unlock set for candidate evaluation.
    /// </summary>
    public IReadOnlySet<string> UnlockedSet =>
        new HashSet<string>(UnlockedIds, StringComparer.Ordinal);

    /// <summary>
    /// Returns a document with additional catalog IDs merged in sorted order.
    /// Unknown IDs are rejected so corrupt progression cannot invent unlocks.
    /// </summary>
    public AchievementsDocument WithUnlocks(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var merged = new SortedSet<string>(UnlockedIds, StringComparer.Ordinal);
        foreach (var id in ids)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (AchievementCatalog.Find(id) is null)
            {
                throw new ArgumentException(
                    "Unknown achievement id: " + id,
                    nameof(ids));
            }

            merged.Add(id);
        }

        if (merged.Count > MaximumUnlockCount)
        {
            throw new InvalidOperationException(
                "Unlock count exceeds the achievements document capacity.");
        }

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            UnlockedIds = merged.ToArray(),
        };
    }

    public string SerializeCanonical()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteStartArray("unlockedIds");
            foreach (var id in UnlockedIds)
            {
                writer.WriteStringValue(id);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static AchievementsLoadResult Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AchievementsLoadResult(
                AchievementsLoadCode.Empty,
                "Achievements document is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new AchievementsLoadResult(
                AchievementsLoadCode.InvalidJson,
                "Achievements JSON is invalid: " + exception.Message);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new AchievementsLoadResult(
                    AchievementsLoadCode.InvalidJson,
                    "Achievements root must be an object.");
            }

            try
            {
                return ReadObject(document.RootElement);
            }
            catch (InvalidDataException exception)
            {
                return new AchievementsLoadResult(
                    AchievementsLoadCode.InvalidField,
                    exception.Message);
            }
        }
    }

    private static AchievementsLoadResult ReadObject(JsonElement root)
    {
        if ((!root.TryGetProperty("schemaVersion", out var schemaElement)
                && !root.TryGetProperty("schema_version", out schemaElement))
            || schemaElement.ValueKind != JsonValueKind.Number
            || !schemaElement.TryGetInt32(out var schemaVersion))
        {
            return new AchievementsLoadResult(
                AchievementsLoadCode.InvalidField,
                "schemaVersion must be an integer.");
        }

        if (schemaVersion > CurrentSchemaVersion)
        {
            return new AchievementsLoadResult(
                AchievementsLoadCode.UnsupportedSchema,
                $"Achievements schema_version {schemaVersion} is newer than supported {CurrentSchemaVersion}.");
        }

        if (schemaVersion < 1)
        {
            return new AchievementsLoadResult(
                AchievementsLoadCode.UnsupportedSchema,
                $"Achievements schema_version {schemaVersion} is unsupported.");
        }

        if (!root.TryGetProperty("unlockedIds", out var unlockedElement)
            || unlockedElement.ValueKind != JsonValueKind.Array)
        {
            return new AchievementsLoadResult(
                AchievementsLoadCode.InvalidField,
                "unlockedIds must be an array.");
        }

        var count = unlockedElement.GetArrayLength();
        if (count > MaximumUnlockCount)
        {
            return new AchievementsLoadResult(
                AchievementsLoadCode.InvalidField,
                "unlockedIds exceeds the achievements document capacity.");
        }

        var unlocked = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var element in unlockedElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                return new AchievementsLoadResult(
                    AchievementsLoadCode.InvalidField,
                    "unlockedIds entries must be strings.");
            }

            var id = element.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                return new AchievementsLoadResult(
                    AchievementsLoadCode.InvalidField,
                    "unlockedIds entries cannot be empty.");
            }

            if (AchievementCatalog.Find(id) is null)
            {
                return new AchievementsLoadResult(
                    AchievementsLoadCode.InvalidField,
                    "Unknown achievement id: " + id);
            }

            unlocked.Add(id);
        }

        return new AchievementsLoadResult(
            AchievementsLoadCode.Success,
            "Achievements document loaded.",
            new AchievementsDocument(CurrentSchemaVersion, unlocked.ToArray()));
    }
}

/// <summary>
/// Atomic achievements store under an absolute user-data root.
/// </summary>
public sealed class AchievementsStore
{
    public AchievementsStore(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        AchievementsPath = Path.Combine(UserDataRoot, AchievementsDocument.FileName);
    }

    public string UserDataRoot { get; }

    public string AchievementsPath { get; }

    public AchievementsLoadResult Load()
    {
        if (!File.Exists(AchievementsPath))
        {
            return new AchievementsLoadResult(
                AchievementsLoadCode.Success,
                "Achievements defaults applied.",
                AchievementsDocument.CreateDefaults());
        }

        try
        {
            return AchievementsDocument.Read(File.ReadAllText(AchievementsPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new AchievementsLoadResult(
                AchievementsLoadCode.IoError,
                "Achievements file could not be read: " + exception.Message);
        }
    }

    public void Save(AchievementsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(UserDataRoot);
        var payload = document with { SchemaVersion = AchievementsDocument.CurrentSchemaVersion };
        // Normalize through WithUnlocks([]) so ids stay sorted unique.
        payload = payload.WithUnlocks(Array.Empty<string>());
        var temporaryPath = AchievementsPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            payload.SerializeCanonical(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, AchievementsPath, overwrite: true);
    }
}
