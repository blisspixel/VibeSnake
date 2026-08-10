namespace VibeSnake.Persistence;

/// <summary>
/// Parses schema-1 binding tokens such as <c>key:up</c> and <c>button:south</c>
/// into typed kinds without referencing Godot or other presentation layers.
/// </summary>
public enum InputBindingKind : byte
{
    Key = 0,
    Button = 1,
    Axis = 2,
}

public readonly record struct ParsedInputBinding(
    InputBindingKind Kind,
    string Identifier,
    float AxisValue = 0.0f);

public static class InputBindingToken
{
    private static readonly HashSet<string> KnownKeyboardDefaultTokens = new(
        [
            "key:up",
            "key:down",
            "key:left",
            "key:right",
            "key:enter",
            "key:escape",
            "key:p",
            "key:f8",
            "key:space",
            "key:w",
            "key:a",
            "key:s",
            "key:d",
            "key:r",
            "key:q",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> SupportedKeyboardIdentifiers = new(
        [
            "up",
            "down",
            "left",
            "right",
            "enter",
            "return",
            "escape",
            "esc",
            "space",
            "tab",
            "backspace",
            "delete",
            "home",
            "end",
            "insert",
            "minus",
            "equal",
            "comma",
            "period",
            "slash",
            "semicolon",
            "apostrophe",
            "f1",
            "f2",
            "f3",
            "f4",
            "f5",
            "f6",
            "f7",
            "f8",
            "f9",
            "f10",
            "f11",
            "f12",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> SupportedControllerButtons = new(
        [
            "dpad_up",
            "dpad_down",
            "dpad_left",
            "dpad_right",
            "south",
            "east",
            "west",
            "north",
            "a",
            "b",
            "x",
            "y",
            "start",
            "select",
            "back",
            "guide",
            "left_stick",
            "right_stick",
            "left_shoulder",
            "right_shoulder",
            "misc1",
            "paddle1",
            "paddle2",
            "paddle3",
            "paddle4",
            "touchpad",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> SupportedControllerAxes = new(
        [
            "left_x",
            "left_y",
            "right_x",
            "right_y",
            "left_trigger",
            "right_trigger",
        ],
        StringComparer.Ordinal);

    private static readonly Dictionary<string, string> CanonicalKeyAliases = new(
        StringComparer.Ordinal)
    {
        ["return"] = "enter",
        ["esc"] = "escape",
    };

    private static readonly Dictionary<string, string> CanonicalButtonAliases = new(
        StringComparer.Ordinal)
    {
        ["a"] = "south",
        ["b"] = "east",
        ["x"] = "west",
        ["y"] = "north",
        ["back"] = "select",
    };

    public static bool TryParse(string token, out ParsedInputBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        var separator = trimmed.IndexOf(':');
        if (separator <= 0 || separator >= trimmed.Length - 1)
        {
            return false;
        }

        var kindText = trimmed[..separator];
        var remainder = trimmed[(separator + 1)..];
        if (string.Equals(kindText, "key", StringComparison.Ordinal))
        {
            if (!IsSafeIdentifier(remainder))
            {
                return false;
            }

            binding = new ParsedInputBinding(InputBindingKind.Key, remainder.ToLowerInvariant());
            return true;
        }

        if (string.Equals(kindText, "button", StringComparison.Ordinal))
        {
            if (!IsSafeIdentifier(remainder))
            {
                return false;
            }

            binding = new ParsedInputBinding(InputBindingKind.Button, remainder.ToLowerInvariant());
            return true;
        }

        if (string.Equals(kindText, "axis", StringComparison.Ordinal))
        {
            // axis:left_x:+1 or axis:left_y:-1
            var parts = remainder.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !IsSafeIdentifier(parts[0]))
            {
                return false;
            }

            if (!float.TryParse(
                    parts[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var axisValue)
                || !float.IsFinite(axisValue)
                || axisValue is 0.0f or < -1.0f or > 1.0f)
            {
                return false;
            }

            binding = new ParsedInputBinding(
                InputBindingKind.Axis,
                parts[0].ToLowerInvariant(),
                axisValue);
            return true;
        }

        return false;
    }

    public static bool IsKnownKeyboardDefaultToken(string token) =>
        KnownKeyboardDefaultTokens.Contains(token);

    /// <summary>
    /// Returns whether a parsed token belongs to the stable keyboard vocabulary
    /// understood by the native Godot adapter.
    /// </summary>
    public static bool IsSupportedKeyboardBinding(ParsedInputBinding binding)
    {
        if (binding.Kind != InputBindingKind.Key)
        {
            return false;
        }

        var identifier = binding.Identifier;
        if (identifier.Length == 1
            && (char.IsAsciiLetterOrDigit(identifier[0])))
        {
            return true;
        }

        return SupportedKeyboardIdentifiers.Contains(identifier);
    }

    /// <summary>
    /// Returns whether a parsed token belongs to the stable controller vocabulary
    /// understood by the native Godot adapter.
    /// </summary>
    public static bool IsSupportedControllerBinding(ParsedInputBinding binding) =>
        binding.Kind switch
        {
            InputBindingKind.Button => SupportedControllerButtons.Contains(binding.Identifier),
            InputBindingKind.Axis => SupportedControllerAxes.Contains(binding.Identifier),
            _ => false,
        };

    /// <summary>
    /// Returns the physical conflict identity. Axis thresholds with the same axis
    /// and direction intentionally conflict because pressing farther would fire both.
    /// </summary>
    public static string GetConflictKey(ParsedInputBinding binding) =>
        binding.Kind switch
        {
            InputBindingKind.Key => "key:" + CanonicalKeyIdentifier(binding.Identifier),
            InputBindingKind.Button => "button:" + CanonicalButtonIdentifier(binding.Identifier),
            InputBindingKind.Axis => "axis:" + binding.Identifier
                + (binding.AxisValue < 0.0f ? ":-" : ":+"),
            _ => throw new ArgumentOutOfRangeException(nameof(binding)),
        };

    private static string CanonicalKeyIdentifier(string identifier) =>
        CanonicalKeyAliases.GetValueOrDefault(identifier, identifier);

    private static string CanonicalButtonIdentifier(string identifier) =>
        CanonicalButtonAliases.GetValueOrDefault(identifier, identifier);

    private static bool IsSafeIdentifier(string value)
    {
        if (value.Length is 0 or > 32)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
