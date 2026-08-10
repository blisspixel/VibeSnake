using System.Buffers;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Persistence;

public enum OnboardingStatus : byte
{
    NotStarted = 0,
    Skipped = 1,
    Completed = 2,
}

public enum OnboardingLoadCode : byte
{
    Success = 0,
    Empty = 1,
    InvalidJson = 2,
    UnsupportedSchema = 3,
    InvalidField = 4,
    IoError = 5,
}

public sealed record OnboardingLoadResult(
    OnboardingLoadCode Code,
    string Message,
    OnboardingProgressDocument? Document = null,
    bool IsNewProfile = false)
{
    public bool IsSuccess => Code == OnboardingLoadCode.Success && Document is not null;
}

/// <summary>
/// Profile-local tutorial decision. Lesson simulation stays transient and
/// unscored; only the offer outcome is persisted.
/// </summary>
public sealed record OnboardingProgressDocument(
    int SchemaVersion,
    OnboardingStatus Status,
    int TutorialRevision)
{
    public const int CurrentSchemaVersion = 1;
    public const int CurrentTutorialRevision = 1;
    public const string FileName = "onboarding.json";

    public static OnboardingProgressDocument CreateDefaults() => new(
        CurrentSchemaVersion,
        OnboardingStatus.NotStarted,
        CurrentTutorialRevision);

    public OnboardingProgressDocument WithStatus(OnboardingStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Status = status,
            TutorialRevision = CurrentTutorialRevision,
        };
    }

    public string SerializeCanonical()
    {
        Validate(this);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("status", StatusToWire(Status));
            writer.WriteNumber("tutorialRevision", CurrentTutorialRevision);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    public static OnboardingLoadResult Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new OnboardingLoadResult(
                OnboardingLoadCode.Empty,
                "Onboarding document is empty.");
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new OnboardingLoadResult(
                OnboardingLoadCode.InvalidJson,
                "Onboarding JSON is invalid: " + exception.Message);
        }

        using (parsed)
        {
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new OnboardingLoadResult(
                    OnboardingLoadCode.InvalidField,
                    "Onboarding root must be an object.");
            }

            if (!root.TryGetProperty("schemaVersion", out var schemaElement)
                || schemaElement.ValueKind != JsonValueKind.Number
                || !schemaElement.TryGetInt32(out var schemaVersion))
            {
                return new OnboardingLoadResult(
                    OnboardingLoadCode.InvalidField,
                    "schemaVersion must be an integer.");
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                return new OnboardingLoadResult(
                    OnboardingLoadCode.UnsupportedSchema,
                    $"Onboarding schemaVersion {schemaVersion} is unsupported.");
            }

            if (!root.TryGetProperty("status", out var statusElement)
                || statusElement.ValueKind != JsonValueKind.String
                || !TryParseStatus(statusElement.GetString(), out var status))
            {
                return new OnboardingLoadResult(
                    OnboardingLoadCode.InvalidField,
                    "status must be not-started, skipped, or completed.");
            }

            if (!root.TryGetProperty("tutorialRevision", out var revisionElement)
                || revisionElement.ValueKind != JsonValueKind.Number
                || !revisionElement.TryGetInt32(out var revision)
                || revision != CurrentTutorialRevision)
            {
                return new OnboardingLoadResult(
                    OnboardingLoadCode.InvalidField,
                    $"tutorialRevision must be {CurrentTutorialRevision}.");
            }

            return new OnboardingLoadResult(
                OnboardingLoadCode.Success,
                "Onboarding document loaded.",
                new OnboardingProgressDocument(schemaVersion, status, revision));
        }
    }

    private static void Validate(OnboardingProgressDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion
            || document.TutorialRevision != CurrentTutorialRevision
            || !Enum.IsDefined(document.Status))
        {
            throw new InvalidDataException("Onboarding document is not canonical.");
        }
    }

    private static string StatusToWire(OnboardingStatus status) => status switch
    {
        OnboardingStatus.NotStarted => "not-started",
        OnboardingStatus.Skipped => "skipped",
        OnboardingStatus.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static bool TryParseStatus(string? wire, out OnboardingStatus status)
    {
        status = wire switch
        {
            "not-started" => OnboardingStatus.NotStarted,
            "skipped" => OnboardingStatus.Skipped,
            "completed" => OnboardingStatus.Completed,
            _ => (OnboardingStatus)byte.MaxValue,
        };
        return Enum.IsDefined(status);
    }
}

/// <summary>Atomic onboarding progress store under one absolute player root.</summary>
public sealed class OnboardingStore
{
    public OnboardingStore(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        OnboardingPath = Path.Combine(UserDataRoot, OnboardingProgressDocument.FileName);
    }

    public string UserDataRoot { get; }

    public string OnboardingPath { get; }

    public OnboardingLoadResult Load()
    {
        if (!File.Exists(OnboardingPath))
        {
            return new OnboardingLoadResult(
                OnboardingLoadCode.Success,
                "New profile requires an onboarding decision.",
                OnboardingProgressDocument.CreateDefaults(),
                IsNewProfile: true);
        }

        try
        {
            return OnboardingProgressDocument.Read(File.ReadAllText(OnboardingPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new OnboardingLoadResult(
                OnboardingLoadCode.IoError,
                "Onboarding file could not be read: " + exception.Message);
        }
    }

    public void Save(OnboardingProgressDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(UserDataRoot);
        var temporaryPath = OnboardingPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            document.SerializeCanonical(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, OnboardingPath, overwrite: true);
    }
}
