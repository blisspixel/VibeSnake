namespace VibeSnake.Persistence;

/// <summary>
/// Stable prompt families used to describe logical input without shipping
/// platform-specific controller artwork in the rules or persistence layers.
/// </summary>
public enum InputPromptFamily : byte
{
    Keyboard = 0,
    GenericController = 1,
    Xbox = 2,
    PlayStation = 3,
    Nintendo = 4,
}

public enum InputPromptGlyphShape : byte
{
    Unbound = 0,
    Keycap = 1,
    FaceButton = 2,
    Shoulder = 3,
    DirectionalPad = 4,
    Stick = 5,
    Trigger = 6,
    SystemButton = 7,
}

public readonly record struct InputPromptGlyphDescriptor(
    bool IsBound,
    string Label,
    InputPromptGlyphShape Shape);

public static class InputPromptGlyphs
{
    private static readonly Dictionary<string, string> KeyLabels = new(StringComparer.Ordinal)
    {
        ["up"] = "Up",
        ["down"] = "Down",
        ["left"] = "Left",
        ["right"] = "Right",
        ["enter"] = "Enter",
        ["escape"] = "Esc",
        ["space"] = "Space",
        ["backspace"] = "Backspace",
        ["delete"] = "Delete",
        ["home"] = "Home",
        ["end"] = "End",
        ["insert"] = "Insert",
        ["minus"] = "-",
        ["equal"] = "=",
        ["comma"] = ",",
        ["period"] = ".",
        ["slash"] = "/",
        ["semicolon"] = ";",
        ["apostrophe"] = "'",
    };

    private static readonly Dictionary<string, string> ButtonLabels = new(StringComparer.Ordinal)
    {
        ["left_stick"] = "L3",
        ["right_stick"] = "R3",
        ["dpad_up"] = "D-pad Up",
        ["dpad_down"] = "D-pad Down",
        ["dpad_left"] = "D-pad Left",
        ["dpad_right"] = "D-pad Right",
        ["guide"] = "Guide",
        ["misc1"] = "Misc",
        ["paddle1"] = "Paddle 1",
        ["paddle2"] = "Paddle 2",
        ["paddle3"] = "Paddle 3",
        ["paddle4"] = "Paddle 4",
        ["touchpad"] = "Touchpad",
    };

    private static readonly Dictionary<(string Identifier, InputPromptFamily Family), string>
        FamilyButtonLabels = new()
        {
            [("south", InputPromptFamily.Xbox)] = "A",
            [("south", InputPromptFamily.PlayStation)] = "Cross",
            [("south", InputPromptFamily.Nintendo)] = "B",
            [("east", InputPromptFamily.Xbox)] = "B",
            [("east", InputPromptFamily.PlayStation)] = "Circle",
            [("east", InputPromptFamily.Nintendo)] = "A",
            [("west", InputPromptFamily.Xbox)] = "X",
            [("west", InputPromptFamily.PlayStation)] = "Square",
            [("west", InputPromptFamily.Nintendo)] = "Y",
            [("north", InputPromptFamily.Xbox)] = "Y",
            [("north", InputPromptFamily.PlayStation)] = "Triangle",
            [("north", InputPromptFamily.Nintendo)] = "X",
            [("start", InputPromptFamily.Xbox)] = "Menu",
            [("start", InputPromptFamily.PlayStation)] = "Options",
            [("start", InputPromptFamily.Nintendo)] = "+",
            [("select", InputPromptFamily.Xbox)] = "View",
            [("select", InputPromptFamily.PlayStation)] = "Create",
            [("select", InputPromptFamily.Nintendo)] = "-",
            [("left_shoulder", InputPromptFamily.Xbox)] = "LB",
            [("left_shoulder", InputPromptFamily.PlayStation)] = "L1",
            [("left_shoulder", InputPromptFamily.Nintendo)] = "L",
            [("right_shoulder", InputPromptFamily.Xbox)] = "RB",
            [("right_shoulder", InputPromptFamily.PlayStation)] = "R1",
            [("right_shoulder", InputPromptFamily.Nintendo)] = "R",
        };

    private static readonly Dictionary<string, string> GenericButtonLabels = new(
        StringComparer.Ordinal)
    {
        ["south"] = "South",
        ["east"] = "East",
        ["west"] = "West",
        ["north"] = "North",
        ["start"] = "Start",
        ["select"] = "Select",
        ["left_shoulder"] = "Left Shoulder",
        ["right_shoulder"] = "Right Shoulder",
    };

    private static readonly Dictionary<string, string> ButtonAliases = new(StringComparer.Ordinal)
    {
        ["a"] = "south",
        ["b"] = "east",
        ["x"] = "west",
        ["y"] = "north",
        ["back"] = "select",
    };

    private static readonly HashSet<string> FaceButtons = new(
        ["south", "east", "west", "north"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> ShoulderButtons = new(
        ["left_shoulder", "right_shoulder", "paddle1", "paddle2", "paddle3", "paddle4"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> StickButtons = new(
        ["left_stick", "right_stick"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> TriggerAxes = new(
        ["left_trigger", "right_trigger"],
        StringComparer.Ordinal);

    private static readonly Dictionary<(string Identifier, bool Negative), string> AxisLabels =
        new()
        {
            [("left_x", true)] = "Left Stick Left",
            [("left_x", false)] = "Left Stick Right",
            [("left_y", true)] = "Left Stick Up",
            [("left_y", false)] = "Left Stick Down",
            [("right_x", true)] = "Right Stick Left",
            [("right_x", false)] = "Right Stick Right",
            [("right_y", true)] = "Right Stick Up",
            [("right_y", false)] = "Right Stick Down",
            [("left_trigger", true)] = "Left Trigger",
            [("left_trigger", false)] = "Left Trigger",
            [("right_trigger", true)] = "Right Trigger",
            [("right_trigger", false)] = "Right Trigger",
        };

    public static InputPromptFamily DetectControllerFamily(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return InputPromptFamily.GenericController;
        }

        var name = deviceName.Trim();
        if (ContainsAny(name, "nintendo", "switch", "joy-con", "joycon", "pro controller"))
        {
            return InputPromptFamily.Nintendo;
        }

        if (ContainsAny(name, "playstation", "dualshock", "dualsense", "sony", "ps3", "ps4", "ps5"))
        {
            return InputPromptFamily.PlayStation;
        }

        if (ContainsAny(name, "xbox", "xinput", "microsoft"))
        {
            return InputPromptFamily.Xbox;
        }

        return InputPromptFamily.GenericController;
    }

    public static string FormatToken(string token, InputPromptFamily family)
    {
        var descriptor = DescribeToken(token, family);
        return "[" + descriptor.Label + "]";
    }

    /// <summary>
    /// Resolves stable label and vector-badge shape semantics independently of
    /// Godot drawing APIs. Text remains the accessibility fallback and is always
    /// present inside the illustrated badge.
    /// </summary>
    public static InputPromptGlyphDescriptor DescribeToken(
        string token,
        InputPromptFamily family)
    {
        if (!InputBindingToken.TryParse(token, out var binding))
        {
            return new InputPromptGlyphDescriptor(
                IsBound: false,
                Label: "Unbound",
                Shape: InputPromptGlyphShape.Unbound);
        }

        return new InputPromptGlyphDescriptor(
            IsBound: true,
            Label: binding.Kind switch
            {
                InputBindingKind.Key => FormatKey(binding.Identifier),
                InputBindingKind.Button => FormatButton(binding.Identifier, family),
                InputBindingKind.Axis => FormatAxis(binding.Identifier, binding.AxisValue),
                _ => "Unbound",
            },
            Shape: ResolveShape(binding));
    }

    private static InputPromptGlyphShape ResolveShape(ParsedInputBinding binding)
    {
        if (binding.Kind == InputBindingKind.Key)
        {
            return InputPromptGlyphShape.Keycap;
        }

        if (binding.Kind == InputBindingKind.Axis)
        {
            return TriggerAxes.Contains(binding.Identifier)
                ? InputPromptGlyphShape.Trigger
                : InputPromptGlyphShape.Stick;
        }

        var identifier = CanonicalButtonIdentifier(binding.Identifier);
        if (FaceButtons.Contains(identifier))
        {
            return InputPromptGlyphShape.FaceButton;
        }

        if (identifier.StartsWith("dpad_", StringComparison.Ordinal))
        {
            return InputPromptGlyphShape.DirectionalPad;
        }

        if (ShoulderButtons.Contains(identifier))
        {
            return InputPromptGlyphShape.Shoulder;
        }

        return StickButtons.Contains(identifier)
            ? InputPromptGlyphShape.Stick
            : InputPromptGlyphShape.SystemButton;
    }

    private static string FormatKey(string identifier) =>
        KeyLabels.GetValueOrDefault(identifier, identifier.ToUpperInvariant());

    private static string FormatButton(string identifier, InputPromptFamily family)
    {
        var canonical = CanonicalButtonIdentifier(identifier);
        if (FamilyButtonLabels.TryGetValue((canonical, family), out var familyLabel))
        {
            return familyLabel;
        }

        if (GenericButtonLabels.TryGetValue(canonical, out var genericLabel))
        {
            return genericLabel;
        }

        return ButtonLabels.GetValueOrDefault(canonical, canonical);
    }

    private static string FormatAxis(string identifier, float value)
    {
        var negative = value < 0.0f;
        return AxisLabels.GetValueOrDefault(
            (identifier, negative),
            identifier + (negative ? " -" : " +"));
    }

    private static string CanonicalButtonIdentifier(string identifier) =>
        ButtonAliases.GetValueOrDefault(identifier, identifier);

    private static bool ContainsAny(string value, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
