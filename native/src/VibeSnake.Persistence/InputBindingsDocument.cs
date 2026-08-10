using System.Buffers;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Persistence;

/// <summary>
/// Schema 1 logical input bindings by device class.
/// Defaults always retain Confirm, Back, and RestoreDefaults escape hatches.
/// </summary>
public enum InputBindingsLoadCode : byte
{
    Success = 0,
    Empty = 1,
    InvalidJson = 2,
    UnsupportedSchema = 3,
    InvalidField = 4,
    MissingRequiredAction = 5,
    Conflict = 6,
}

public sealed record InputBindingsLoadResult(
    InputBindingsLoadCode Code,
    string Message,
    InputBindingsDocument? Document = null,
    string? ConflictingAction = null)
{
    public bool IsSuccess => Code == InputBindingsLoadCode.Success && Document is not null;
}

public sealed record InputBindingsDocument(
    int SchemaVersion,
    string DeviceClass,
    IReadOnlyDictionary<string, string> ActionToBinding)
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "input_bindings.json";
    public const string KeyboardDeviceClass = "keyboard";
    public const string ControllerDeviceClass = "controller";

    public static readonly string[] RequiredActions =
    [
        "confirm",
        "back",
        "pause",
        "move_up",
        "move_down",
        "move_left",
        "move_right",
    ];

    public static InputBindingsDocument CreateKeyboardDefaults() => new(
        CurrentSchemaVersion,
        KeyboardDeviceClass,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["move_up"] = "key:up",
            ["move_down"] = "key:down",
            ["move_left"] = "key:left",
            ["move_right"] = "key:right",
            ["confirm"] = "key:enter",
            ["back"] = "key:escape",
            ["pause"] = "key:p",
            ["restore_defaults"] = "key:f8",
        });

    public static InputBindingsDocument CreateControllerDefaults() => new(
        CurrentSchemaVersion,
        ControllerDeviceClass,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["move_up"] = "button:dpad_up",
            ["move_down"] = "button:dpad_down",
            ["move_left"] = "button:dpad_left",
            ["move_right"] = "button:dpad_right",
            ["confirm"] = "button:south",
            ["back"] = "button:east",
            ["pause"] = "button:start",
            ["restore_defaults"] = "button:select",
        });

    public static InputBindingsLoadResult Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.Empty,
                "Input bindings document is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.InvalidJson,
                "Input bindings JSON is invalid: " + exception.Message);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new InputBindingsLoadResult(
                    InputBindingsLoadCode.InvalidField,
                    "Input bindings root must be an object.");
            }

            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schemaElement)
                || schemaElement.ValueKind != JsonValueKind.Number
                || !schemaElement.TryGetInt32(out var schemaVersion))
            {
                return new InputBindingsLoadResult(
                    InputBindingsLoadCode.InvalidField,
                    "schemaVersion must be an integer.");
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                return new InputBindingsLoadResult(
                    InputBindingsLoadCode.UnsupportedSchema,
                    $"Input bindings schemaVersion {schemaVersion} is unsupported.");
            }

            if (!root.TryGetProperty("deviceClass", out var deviceElement)
                || deviceElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(deviceElement.GetString()))
            {
                return new InputBindingsLoadResult(
                    InputBindingsLoadCode.InvalidField,
                    "deviceClass must be a non-empty string.");
            }

            var deviceClass = deviceElement.GetString()!.Trim();
            if (deviceClass is not (KeyboardDeviceClass or ControllerDeviceClass))
            {
                return new InputBindingsLoadResult(
                    InputBindingsLoadCode.InvalidField,
                    $"deviceClass '{deviceClass}' is unsupported.");
            }

            if (!root.TryGetProperty("actions", out var actionsElement)
                || actionsElement.ValueKind != JsonValueKind.Object)
            {
                return new InputBindingsLoadResult(
                    InputBindingsLoadCode.InvalidField,
                    "actions must be an object of action to binding strings.");
            }

            var actions = new Dictionary<string, string>(StringComparer.Ordinal);
            var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in actionsElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return new InputBindingsLoadResult(
                        InputBindingsLoadCode.InvalidField,
                        $"Action '{property.Name}' must map to a non-empty binding string.");
                }

                var binding = property.Value.GetString()!.Trim();
                if (!InputBindingToken.TryParse(binding, out var parsed)
                    || !IsBindingSupported(deviceClass, parsed))
                {
                    return new InputBindingsLoadResult(
                        InputBindingsLoadCode.InvalidField,
                        $"Action '{property.Name}' has an unsupported {deviceClass} binding '{binding}'.");
                }

                var normalizedBinding = NormalizeBindingToken(parsed);
                if (!actions.TryAdd(property.Name, normalizedBinding))
                {
                    return new InputBindingsLoadResult(
                        InputBindingsLoadCode.InvalidField,
                        $"Duplicate action '{property.Name}'.");
                }

                var conflictKey = InputBindingToken.GetConflictKey(parsed);
                if (!reverse.TryAdd(conflictKey, property.Name))
                {
                    return new InputBindingsLoadResult(
                        InputBindingsLoadCode.Conflict,
                        $"Binding '{normalizedBinding}' is assigned to more than one action.",
                        ConflictingAction: reverse[conflictKey]);
                }
            }

            foreach (var required in RequiredActions)
            {
                if (!actions.ContainsKey(required))
                {
                    return new InputBindingsLoadResult(
                        InputBindingsLoadCode.MissingRequiredAction,
                        $"Required action '{required}' is missing.");
                }
            }

            return new InputBindingsLoadResult(
                InputBindingsLoadCode.Success,
                "Input bindings document is valid.",
                new InputBindingsDocument(
                    schemaVersion,
                    deviceClass,
                    actions));
        }
    }

    public string SerializeCanonical()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("deviceClass", DeviceClass);
            writer.WriteStartObject("actions");
            foreach (var pair in ActionToBinding.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WriteString(pair.Key, pair.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    /// <summary>
    /// Returns a new document with one action remapped to a binding token.
    /// Does not mutate this instance. Rejects unknown actions, unparsable tokens,
    /// and bindings already owned by a different action so Confirm, Back, Pause,
    /// and movement cannot silently steal each other's hardware.
    /// </summary>
    public InputBindingsLoadResult TryRemapAction(string action, string bindingToken)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.InvalidField,
                "Action name must be non-empty.");
        }

        var actionName = action.Trim();
        if (!ActionToBinding.ContainsKey(actionName))
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.InvalidField,
                $"Unknown action '{actionName}' cannot be remapped.");
        }

        if (string.IsNullOrWhiteSpace(bindingToken)
            || !InputBindingToken.TryParse(bindingToken, out var parsed))
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.InvalidField,
                "Binding token must be a valid key:, button:, or axis: token.");
        }

        if (!IsBindingSupported(DeviceClass, parsed))
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.InvalidField,
                $"Binding token is not supported for deviceClass '{DeviceClass}'.");
        }

        var normalized = NormalizeBindingToken(parsed);
        if (string.Equals(ActionToBinding[actionName], normalized, StringComparison.Ordinal))
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.Success,
                "Binding unchanged.",
                this);
        }

        foreach (var pair in ActionToBinding)
        {
            if (string.Equals(pair.Key, actionName, StringComparison.Ordinal))
            {
                continue;
            }

            if (InputBindingToken.TryParse(pair.Value, out var existing)
                && string.Equals(
                    InputBindingToken.GetConflictKey(existing),
                    InputBindingToken.GetConflictKey(parsed),
                    StringComparison.Ordinal))
            {
                return new InputBindingsLoadResult(
                    InputBindingsLoadCode.Conflict,
                    $"Binding '{normalized}' is already assigned to action '{pair.Key}'.",
                    ConflictingAction: pair.Key);
            }
        }

        var next = new Dictionary<string, string>(ActionToBinding, StringComparer.Ordinal)
        {
            [actionName] = normalized,
        };
        var document = new InputBindingsDocument(SchemaVersion, DeviceClass, next);
        return new InputBindingsLoadResult(
            InputBindingsLoadCode.Success,
            $"Action '{actionName}' remapped to '{normalized}'.",
            document);
    }

    /// <summary>
    /// Swaps the bindings of two known actions without intermediate conflicts.
    /// Idempotent when the actions already hold each other's tokens.
    /// </summary>
    public InputBindingsLoadResult TrySwapActions(string leftAction, string rightAction)
    {
        if (string.IsNullOrWhiteSpace(leftAction) || string.IsNullOrWhiteSpace(rightAction))
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.InvalidField,
                "Both action names must be non-empty.");
        }

        var left = leftAction.Trim();
        var right = rightAction.Trim();
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.Success,
                "Actions are identical; no swap required.",
                this);
        }

        if (!ActionToBinding.TryGetValue(left, out var leftBinding)
            || !ActionToBinding.TryGetValue(right, out var rightBinding))
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.InvalidField,
                "Both actions must exist before they can be swapped.");
        }

        if (string.Equals(leftBinding, rightBinding, StringComparison.Ordinal))
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.Conflict,
                "Cannot swap actions that already share the same binding token.");
        }

        var next = new Dictionary<string, string>(ActionToBinding, StringComparer.Ordinal)
        {
            [left] = rightBinding,
            [right] = leftBinding,
        };
        var document = new InputBindingsDocument(SchemaVersion, DeviceClass, next);
        return new InputBindingsLoadResult(
            InputBindingsLoadCode.Success,
            $"Swapped bindings for '{left}' and '{right}'.",
            document);
    }

    private static string NormalizeBindingToken(ParsedInputBinding parsed) =>
        parsed.Kind switch
        {
            InputBindingKind.Key or InputBindingKind.Button =>
                InputBindingToken.GetConflictKey(parsed),
            // Preserve fractional axis thresholds with a signed invariant form.
            InputBindingKind.Axis => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"axis:{parsed.Identifier}:{(parsed.AxisValue >= 0.0f ? "+" : string.Empty)}{parsed.AxisValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"),
            _ => throw new ArgumentOutOfRangeException(nameof(parsed)),
        };

    private static bool IsBindingSupported(string deviceClass, ParsedInputBinding parsed) =>
        deviceClass switch
        {
            KeyboardDeviceClass => InputBindingToken.IsSupportedKeyboardBinding(parsed),
            ControllerDeviceClass => InputBindingToken.IsSupportedControllerBinding(parsed),
            _ => false,
        };
}

public sealed class InputBindingsStore
{
    public InputBindingsStore(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        BindingsDirectory = Path.Combine(UserDataRoot, "input");
    }

    public string UserDataRoot { get; }

    public string BindingsDirectory { get; }

    public string PathForDeviceClass(string deviceClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceClass);
        var safe = string.Concat(
            deviceClass.Select(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '_'));
        return Path.Combine(BindingsDirectory, safe + "." + InputBindingsDocument.FileName);
    }

    public InputBindingsLoadResult LoadOrDefault(string deviceClass)
    {
        var path = PathForDeviceClass(deviceClass);
        if (!File.Exists(path))
        {
            var defaults = string.Equals(
                deviceClass,
                InputBindingsDocument.ControllerDeviceClass,
                StringComparison.Ordinal)
                ? InputBindingsDocument.CreateControllerDefaults()
                : InputBindingsDocument.CreateKeyboardDefaults();
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.Success,
                "Input binding defaults applied.",
                defaults);
        }

        try
        {
            return InputBindingsDocument.Read(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new InputBindingsLoadResult(
                InputBindingsLoadCode.InvalidField,
                "Input bindings file could not be read: " + exception.Message);
        }
    }

    public void Save(InputBindingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var validation = InputBindingsDocument.Read(document.SerializeCanonical());
        if (!validation.IsSuccess || validation.Document is null)
        {
            throw new InvalidOperationException(
                "Input bindings cannot be saved: " + validation.Message);
        }

        Directory.CreateDirectory(BindingsDirectory);
        var path = PathForDeviceClass(document.DeviceClass);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            validation.Document.SerializeCanonical(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
