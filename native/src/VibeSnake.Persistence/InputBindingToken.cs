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
        token is "key:up"
            or "key:down"
            or "key:left"
            or "key:right"
            or "key:enter"
            or "key:escape"
            or "key:p"
            or "key:f8"
            or "key:space"
            or "key:w"
            or "key:a"
            or "key:s"
            or "key:d"
            or "key:r"
            or "key:q";

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
