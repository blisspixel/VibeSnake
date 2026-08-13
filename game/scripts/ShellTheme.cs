using Godot;

namespace VibeSnake.Game;

/// <summary>
/// Central shell palette and font ownership. Gameplay-specific power colors
/// remain with PowerPresentation; reusable shell chrome resolves here.
/// </summary>
internal sealed class ShellTheme
{
    private static readonly ShellPalette StandardPalette = new(
        CanvasBackground: new Color(0.02f, 0.035f, 0.03f),
        BoardBackground: new Color(0.055f, 0.12f, 0.085f),
        PrimaryText: new Color(0.45f, 1.0f, 0.68f),
        SecondaryText: new Color(0.58f, 0.7f, 0.64f),
        BodyText: Colors.White,
        AccentText: new Color(0.46f, 0.94f, 0.96f),
        GoldText: new Color(0.85f, 0.78f, 0.45f),
        MutedGoldText: new Color(0.7f, 0.65f, 0.4f),
        WarningText: new Color(1.0f, 0.68f, 0.28f),
        SelectedText: new Color(1.0f, 0.92f, 0.45f),
        PromptFill: new Color(0.075f, 0.16f, 0.12f),
        PromptOutline: new Color(0.58f, 0.82f, 0.68f));

    private static readonly ShellPalette HighContrastPalette = new(
        CanvasBackground: Colors.Black,
        BoardBackground: Colors.Black,
        PrimaryText: Colors.White,
        SecondaryText: new Color(0.92f, 0.92f, 0.92f),
        BodyText: Colors.White,
        AccentText: new Color(0.55f, 1.0f, 1.0f),
        GoldText: new Color(1.0f, 0.92f, 0.3f),
        MutedGoldText: new Color(1.0f, 0.86f, 0.5f),
        WarningText: new Color(1.0f, 0.75f, 0.25f),
        SelectedText: Colors.Yellow,
        PromptFill: new Color(0.08f, 0.08f, 0.08f),
        PromptOutline: Colors.White);

    public ShellTheme(Font interfaceFont)
    {
        ArgumentNullException.ThrowIfNull(interfaceFont);
        InterfaceFont = interfaceFont;
    }

    public Font InterfaceFont { get; }

    public static ShellPalette Palette(bool highContrast) =>
        highContrast ? HighContrastPalette : StandardPalette;

    public static double ContrastRatio(Color foreground, Color background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static void AssertQualificationContrast()
    {
        foreach (var palette in new[] { StandardPalette, HighContrastPalette })
        {
            AssertContrast(palette.PrimaryText, palette.CanvasBackground, 4.5, "primary/canvas");
            AssertContrast(palette.SecondaryText, palette.CanvasBackground, 4.5, "secondary/canvas");
            AssertContrast(palette.BodyText, palette.BoardBackground, 4.5, "body/board");
            AssertContrast(palette.WarningText, palette.CanvasBackground, 4.5, "warning/canvas");
            AssertContrast(palette.PromptOutline, palette.PromptFill, 3.0, "prompt outline/fill");
            AssertContrast(palette.BodyText, palette.PromptFill, 4.5, "prompt label/fill");
        }
    }

    private static void AssertContrast(
        Color foreground,
        Color background,
        double minimum,
        string pair)
    {
        var ratio = ContrastRatio(foreground, background);
        if (ratio < minimum)
        {
            throw new InvalidOperationException(
                $"Shell theme contrast {pair} was {ratio:0.00}:1; required {minimum:0.0}:1.");
        }
    }

    private static double RelativeLuminance(Color color) =>
        (0.2126 * Linearize(color.R))
        + (0.7152 * Linearize(color.G))
        + (0.0722 * Linearize(color.B));

    private static double Linearize(float channel) =>
        channel <= 0.04045f
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
}

internal readonly record struct ShellPalette(
    Color CanvasBackground,
    Color BoardBackground,
    Color PrimaryText,
    Color SecondaryText,
    Color BodyText,
    Color AccentText,
    Color GoldText,
    Color MutedGoldText,
    Color WarningText,
    Color SelectedText,
    Color PromptFill,
    Color PromptOutline);
