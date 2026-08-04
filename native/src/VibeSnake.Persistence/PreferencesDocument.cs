using System.Buffers;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Persistence;

/// <summary>
/// Versioned player preferences document (schema 2) for multi-bus audio and
/// accessibility settings. Schema 1 single-volume files migrate on load.
/// </summary>
public enum PreferencesLoadCode : byte
{
    Success = 0,
    Empty = 1,
    InvalidJson = 2,
    UnsupportedSchema = 3,
    InvalidField = 4,
    IoError = 5,
    PathUnsafe = 6,
}

public sealed record PreferencesLoadResult(
    PreferencesLoadCode Code,
    string Message,
    PreferencesDocument? Document = null)
{
    public bool IsSuccess => Code == PreferencesLoadCode.Success && Document is not null;
}

public sealed record PreferencesDocument(
    int SchemaVersion,
    float MasterVolume,
    float MusicVolume,
    float SfxVolume,
    float UiVolume,
    bool MasterMuted,
    bool MusicMuted,
    bool SfxMuted,
    bool UiMuted,
    bool Fullscreen,
    bool ReducedMotion,
    bool HighContrast,
    float TextScale,
    float ScreenShakeIntensity,
    bool FlashFree)
{
    public const int CurrentSchemaVersion = 2;
    public const string FileName = "preferences.json";

    public static PreferencesDocument CreateDefaults() => new(
        SchemaVersion: CurrentSchemaVersion,
        MasterVolume: 0.8f,
        MusicVolume: 0.8f,
        SfxVolume: 0.8f,
        UiVolume: 0.8f,
        MasterMuted: false,
        MusicMuted: false,
        SfxMuted: false,
        UiMuted: false,
        Fullscreen: false,
        ReducedMotion: false,
        HighContrast: false,
        TextScale: 1.0f,
        ScreenShakeIntensity: 1.0f,
        FlashFree: false);

    public PreferencesDocument Clamped() => this with
    {
        MasterVolume = Clamp01(MasterVolume),
        MusicVolume = Clamp01(MusicVolume),
        SfxVolume = Clamp01(SfxVolume),
        UiVolume = Clamp01(UiVolume),
        TextScale = Math.Clamp(TextScale, 0.85f, 1.5f),
        ScreenShakeIntensity = Clamp01(ScreenShakeIntensity),
    };

    public static PreferencesLoadResult Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PreferencesLoadResult(
                PreferencesLoadCode.Empty,
                "Preferences document is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new PreferencesLoadResult(
                PreferencesLoadCode.InvalidJson,
                "Preferences JSON is invalid: " + exception.Message);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new PreferencesLoadResult(
                    PreferencesLoadCode.InvalidField,
                    "Preferences root must be an object.");
            }

            var root = document.RootElement;
            var schemaVersion = 1;
            if (root.TryGetProperty("schema_version", out var schemaElement)
                || root.TryGetProperty("schemaVersion", out schemaElement))
            {
                if (schemaElement.ValueKind != JsonValueKind.Number
                    || !schemaElement.TryGetInt32(out schemaVersion))
                {
                    return new PreferencesLoadResult(
                        PreferencesLoadCode.InvalidField,
                        "schema_version must be an integer.");
                }
            }

            if (schemaVersion > CurrentSchemaVersion)
            {
                return new PreferencesLoadResult(
                    PreferencesLoadCode.UnsupportedSchema,
                    $"Preferences schema_version {schemaVersion} is newer than supported {CurrentSchemaVersion}.");
            }

            if (schemaVersion < 1)
            {
                return new PreferencesLoadResult(
                    PreferencesLoadCode.UnsupportedSchema,
                    $"Preferences schema_version {schemaVersion} is unsupported.");
            }

            try
            {
                if (schemaVersion == 1)
                {
                    return new PreferencesLoadResult(
                        PreferencesLoadCode.Success,
                        "Preferences migrated from schema 1.",
                        MigrateFromSchema1(root).Clamped());
                }

                return new PreferencesLoadResult(
                    PreferencesLoadCode.Success,
                    "Preferences document is valid.",
                    ReadSchema2(root).Clamped());
            }
            catch (InvalidDataException exception)
            {
                return new PreferencesLoadResult(
                    PreferencesLoadCode.InvalidField,
                    exception.Message);
            }
        }
    }

    public string SerializeCanonical()
    {
        var clamped = Clamped();
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteNumber("masterVolume", clamped.MasterVolume);
            writer.WriteNumber("musicVolume", clamped.MusicVolume);
            writer.WriteNumber("sfxVolume", clamped.SfxVolume);
            writer.WriteNumber("uiVolume", clamped.UiVolume);
            writer.WriteBoolean("masterMuted", clamped.MasterMuted);
            writer.WriteBoolean("musicMuted", clamped.MusicMuted);
            writer.WriteBoolean("sfxMuted", clamped.SfxMuted);
            writer.WriteBoolean("uiMuted", clamped.UiMuted);
            writer.WriteBoolean("fullscreen", clamped.Fullscreen);
            writer.WriteBoolean("reducedMotion", clamped.ReducedMotion);
            writer.WriteBoolean("highContrast", clamped.HighContrast);
            writer.WriteNumber("textScale", clamped.TextScale);
            writer.WriteNumber("screenShakeIntensity", clamped.ScreenShakeIntensity);
            writer.WriteBoolean("flashFree", clamped.FlashFree);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    private static PreferencesDocument MigrateFromSchema1(JsonElement root)
    {
        // Schema 1 stored sound enabled + single volume + fullscreen.
        var soundOn = true;
        if (root.TryGetProperty("sound_on", out var soundElement)
            || root.TryGetProperty("soundOn", out soundElement))
        {
            if (soundElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidDataException("Preferences field 'sound_on' must be a boolean.");
            }

            soundOn = soundElement.GetBoolean();
        }

        var volume = 0.8f;
        if (root.TryGetProperty("volume", out var volumeElement))
        {
            if (volumeElement.ValueKind != JsonValueKind.Number
                || !volumeElement.TryGetSingle(out volume)
                || float.IsNaN(volume)
                || float.IsInfinity(volume))
            {
                throw new InvalidDataException("Preferences field 'volume' must be a finite number.");
            }
        }

        var fullscreen = false;
        if (root.TryGetProperty("fullscreen", out var fullscreenElement))
        {
            if (fullscreenElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidDataException("Preferences field 'fullscreen' must be a boolean.");
            }

            fullscreen = fullscreenElement.GetBoolean();
        }

        return CreateDefaults() with
        {
            MasterVolume = volume,
            MusicVolume = volume,
            SfxVolume = volume,
            UiVolume = volume,
            MasterMuted = !soundOn,
            MusicMuted = !soundOn,
            SfxMuted = !soundOn,
            UiMuted = !soundOn,
            Fullscreen = fullscreen,
        };
    }

    private static PreferencesDocument ReadSchema2(JsonElement root) => new(
        SchemaVersion: CurrentSchemaVersion,
        MasterVolume: ReadFloat(root, "masterVolume"),
        MusicVolume: ReadFloat(root, "musicVolume"),
        SfxVolume: ReadFloat(root, "sfxVolume"),
        UiVolume: ReadFloat(root, "uiVolume"),
        MasterMuted: ReadBool(root, "masterMuted"),
        MusicMuted: ReadBool(root, "musicMuted"),
        SfxMuted: ReadBool(root, "sfxMuted"),
        UiMuted: ReadBool(root, "uiMuted"),
        Fullscreen: ReadBool(root, "fullscreen"),
        ReducedMotion: ReadBool(root, "reducedMotion"),
        HighContrast: ReadBool(root, "highContrast"),
        TextScale: ReadFloat(root, "textScale"),
        ScreenShakeIntensity: ReadFloat(root, "screenShakeIntensity"),
        FlashFree: ReadBool(root, "flashFree"));

    private static float ReadFloat(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetSingle(out var value)
            || float.IsNaN(value)
            || float.IsInfinity(value))
        {
            throw new InvalidDataException(
                $"Preferences field '{field}' must be a finite number.");
        }

        return value;
    }

    private static bool ReadBool(JsonElement root, string field, bool defaultValue = false)
    {
        if (!root.TryGetProperty(field, out var element))
        {
            return defaultValue;
        }

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"Preferences field '{field}' must be a boolean.");
        }

        return element.GetBoolean();
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0.0f, 1.0f);
}

/// <summary>
/// Atomic preferences store under an absolute user-data root.
/// </summary>
public sealed class PreferencesStore
{
    public PreferencesStore(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        PreferencesPath = Path.Combine(UserDataRoot, PreferencesDocument.FileName);
    }

    public string UserDataRoot { get; }

    public string PreferencesPath { get; }

    public PreferencesLoadResult Load()
    {
        if (!File.Exists(PreferencesPath))
        {
            return new PreferencesLoadResult(
                PreferencesLoadCode.Success,
                "Preferences defaults applied.",
                PreferencesDocument.CreateDefaults());
        }

        try
        {
            return PreferencesDocument.Read(File.ReadAllText(PreferencesPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PreferencesLoadResult(
                PreferencesLoadCode.IoError,
                "Preferences file could not be read: " + exception.Message);
        }
    }

    public void Save(PreferencesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(UserDataRoot);
        var payload = document.Clamped() with { SchemaVersion = PreferencesDocument.CurrentSchemaVersion };
        var temporaryPath = PreferencesPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            payload.SerializeCanonical(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, PreferencesPath, overwrite: true);
    }
}
