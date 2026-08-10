using System.Buffers;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Persistence;

/// <summary>
/// Versioned player preferences document (schema 7) for gameplay, multi-bus
/// audio, accessibility, and controller settings. Older schemas migrate on load.
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
    bool FlashFree,
    float ControllerDeadzone,
    bool MonoOutput,
    bool VibeAdaptationEnabled,
    bool LocalPlaytestSummariesEnabled,
    string WindowMode,
    string WindowSizePreset)
{
    public const int CurrentSchemaVersion = 7;
    public const string FileName = "preferences.json";
    public const string WindowedMode = "windowed";
    public const string BorderlessMode = "borderless";
    public const string ExclusiveFullscreenMode = "exclusive-fullscreen";
    public const string ClassicWindowSize = "classic-4-3";
    public const string HdWindowSize = "hd-16-9";
    public const string DesktopWindowSize = "desktop-16-10";
    public const string FullHdWindowSize = "full-hd-16-9";
    public const float MinimumControllerDeadzone = 0.1f;
    public const float MaximumControllerDeadzone = 0.9f;
    public const float DefaultControllerDeadzone = 0.5f;

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
        FlashFree: false,
        ControllerDeadzone: DefaultControllerDeadzone,
        MonoOutput: false,
        VibeAdaptationEnabled: true,
        LocalPlaytestSummariesEnabled: false,
        WindowMode: WindowedMode,
        WindowSizePreset: HdWindowSize);

    public PreferencesDocument Clamped() => this with
    {
        MasterVolume = Clamp01(MasterVolume),
        MusicVolume = Clamp01(MusicVolume),
        SfxVolume = Clamp01(SfxVolume),
        UiVolume = Clamp01(UiVolume),
        TextScale = Math.Clamp(TextScale, 0.85f, 1.5f),
        ScreenShakeIntensity = Clamp01(ScreenShakeIntensity),
        ControllerDeadzone = Math.Clamp(
            ControllerDeadzone,
            MinimumControllerDeadzone,
            MaximumControllerDeadzone),
        WindowMode = NormalizeWindowMode(WindowMode, Fullscreen),
        WindowSizePreset = NormalizeWindowSizePreset(WindowSizePreset),
        Fullscreen = NormalizeWindowMode(WindowMode, Fullscreen) != WindowedMode,
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

                if (schemaVersion == 2)
                {
                    return new PreferencesLoadResult(
                        PreferencesLoadCode.Success,
                        "Preferences migrated from schema 2.",
                        ReadSchema2(root).Clamped());
                }

                if (schemaVersion == 3)
                {
                    return new PreferencesLoadResult(
                        PreferencesLoadCode.Success,
                        "Preferences migrated from schema 3.",
                        ReadSchema3(root).Clamped());
                }

                if (schemaVersion == 4)
                {
                    return new PreferencesLoadResult(
                        PreferencesLoadCode.Success,
                        "Preferences migrated from schema 4.",
                        ReadSchema4(root).Clamped());
                }

                if (schemaVersion == 5)
                {
                    return new PreferencesLoadResult(
                        PreferencesLoadCode.Success,
                        "Preferences migrated from schema 5.",
                        ReadSchema5(root).Clamped());
                }

                if (schemaVersion == 6)
                {
                    var migrated = ReadSchema6(root);
                    return new PreferencesLoadResult(
                        PreferencesLoadCode.Success,
                        "Preferences migrated from schema 6.",
                        (migrated with
                        {
                            WindowMode = migrated.Fullscreen
                                ? BorderlessMode
                                : WindowedMode,
                        }).Clamped());
                }

                return new PreferencesLoadResult(
                    PreferencesLoadCode.Success,
                    "Preferences document is valid.",
                    ReadSchema7(root).Clamped());
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
            writer.WriteNumber("controllerDeadzone", clamped.ControllerDeadzone);
            writer.WriteBoolean("monoOutput", clamped.MonoOutput);
            writer.WriteBoolean("vibeAdaptationEnabled", clamped.VibeAdaptationEnabled);
            writer.WriteBoolean(
                "localPlaytestSummariesEnabled",
                clamped.LocalPlaytestSummariesEnabled);
            writer.WriteString("windowMode", clamped.WindowMode);
            writer.WriteString("windowSizePreset", clamped.WindowSizePreset);
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
        FlashFree: ReadBool(root, "flashFree"),
        ControllerDeadzone: DefaultControllerDeadzone,
        MonoOutput: false,
        VibeAdaptationEnabled: true,
        LocalPlaytestSummariesEnabled: false,
        WindowMode: WindowedMode,
        WindowSizePreset: ClassicWindowSize);

    private static PreferencesDocument ReadSchema3(JsonElement root) =>
        ReadSchema2(root) with
        {
            ControllerDeadzone = ReadFloat(root, "controllerDeadzone"),
        };

    private static PreferencesDocument ReadSchema4(JsonElement root) =>
        ReadSchema3(root) with
        {
            MonoOutput = ReadBool(root, "monoOutput"),
        };

    private static PreferencesDocument ReadSchema5(JsonElement root) =>
        ReadSchema4(root) with
        {
            VibeAdaptationEnabled = ReadBool(
                root,
                "vibeAdaptationEnabled",
                defaultValue: true),
        };

    private static PreferencesDocument ReadSchema6(JsonElement root) =>
        ReadSchema5(root) with
        {
            LocalPlaytestSummariesEnabled = ReadBool(
                root,
                "localPlaytestSummariesEnabled"),
        };

    private static PreferencesDocument ReadSchema7(JsonElement root) =>
        ReadSchema6(root) with
        {
            WindowMode = ReadChoice(
                root,
                "windowMode",
                [WindowedMode, BorderlessMode, ExclusiveFullscreenMode]),
            WindowSizePreset = ReadChoice(
                root,
                "windowSizePreset",
                [ClassicWindowSize, HdWindowSize, DesktopWindowSize, FullHdWindowSize]),
        };

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

    private static string ReadChoice(
        JsonElement root,
        string field,
        IReadOnlyCollection<string> allowed)
    {
        if (!root.TryGetProperty(field, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"Preferences field '{field}' must be a string.");
        }

        var value = element.GetString();
        if (value is null || !allowed.Contains(value, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Preferences field '{field}' is not a supported value.");
        }

        return value;
    }

    private static string NormalizeWindowMode(string? value, bool fullscreen) => value switch
    {
        WindowedMode when fullscreen => BorderlessMode,
        WindowedMode or BorderlessMode or ExclusiveFullscreenMode => value,
        _ => fullscreen ? BorderlessMode : WindowedMode,
    };

    private static string NormalizeWindowSizePreset(string? value) => value switch
    {
        ClassicWindowSize or HdWindowSize or DesktopWindowSize or FullHdWindowSize => value,
        _ => ClassicWindowSize,
    };

    private static float Clamp01(float value) => Math.Clamp(value, 0.0f, 1.0f);
}

/// <summary>
/// Atomic preferences store under an absolute user-data root.
/// </summary>
public sealed class PreferencesStore
{
    private readonly IPreferencesWriteOperations _writeOperations;

    public PreferencesStore(string userDataRoot)
        : this(userDataRoot, PhysicalPreferencesWriteOperations.Instance)
    {
    }

    internal PreferencesStore(
        string userDataRoot,
        IPreferencesWriteOperations writeOperations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        ArgumentNullException.ThrowIfNull(writeOperations);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        PreferencesPath = Path.Combine(UserDataRoot, PreferencesDocument.FileName);
        _writeOperations = writeOperations;
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
        _writeOperations.CreateDirectory(UserDataRoot);
        var payload = document.Clamped() with { SchemaVersion = PreferencesDocument.CurrentSchemaVersion };
        var temporaryPath = PreferencesPath + ".tmp";
        _writeOperations.WriteAllText(
            temporaryPath,
            payload.SerializeCanonical(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _writeOperations.Move(temporaryPath, PreferencesPath, overwrite: true);
    }
}

internal interface IPreferencesWriteOperations
{
    void CreateDirectory(string path);

    void WriteAllText(string path, string contents, Encoding encoding);

    void Move(string sourcePath, string destinationPath, bool overwrite);
}

internal sealed class PhysicalPreferencesWriteOperations : IPreferencesWriteOperations
{
    public static PhysicalPreferencesWriteOperations Instance { get; } = new();

    private PhysicalPreferencesWriteOperations()
    {
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteAllText(string path, string contents, Encoding encoding) =>
        File.WriteAllText(path, contents, encoding);

    public void Move(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);
}
