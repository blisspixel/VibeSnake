using Godot;

namespace VibeSnake.Game;

/// <summary>
/// Central shell palette and font ownership. Gameplay-specific power colors
/// remain with PowerPresentation; reusable shell chrome resolves here.
/// Standard chrome is one dark phosphor field. Focus uses the cyan accent,
/// and shell or menu colours cannot sit in the magenta hue band.
/// </summary>
internal sealed class ShellTheme
{
    private static readonly ShellPalette StandardPalette = new(
        CanvasBackground: new Color(0.02f, 0.035f, 0.03f),
        BoardBackground: new Color(0.055f, 0.12f, 0.085f),
        PrimaryText: Rgb(0x9F, 0xE0, 0xB4),
        SecondaryText: new Color(0.58f, 0.7f, 0.64f),
        BodyText: Rgb(0xE6, 0xEF, 0xE9),
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

    public static Oklch StandardPrimaryOklch() => ToOklch(StandardPalette.PrimaryText);

    public static double ContrastRatio(Color foreground, Color background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static Color MenuBackdrop(bool highContrast) =>
        highContrast ? Colors.Black : StandardPalette.CanvasBackground;

    public static Color MenuGrid(bool highContrast) =>
        highContrast
            ? new Color(0.15f, 0.15f, 0.15f, 0.5f)
            : new Color(
                StandardPalette.SecondaryText.R,
                StandardPalette.SecondaryText.G,
                StandardPalette.SecondaryText.B,
                0.16f);

    public static Color MenuFrame(ShellPalette palette, bool highContrast) =>
        highContrast ? palette.PrimaryText : palette.SecondaryText;

    public static ShellMenuRowPaint MenuRow(
        ShellPalette palette,
        bool highContrast,
        bool selected,
        bool quitsSession)
    {
        if (highContrast)
        {
            var mark = selected ? palette.SelectedText : palette.PromptOutline;
            return new ShellMenuRowPaint(
                mark,
                selected ? palette.SelectedText : palette.BodyText,
                mark);
        }

        return new ShellMenuRowPaint(
            quitsSession
                ? palette.WarningText
                : selected ? palette.AccentText : palette.SecondaryText,
            selected ? palette.AccentText : palette.BodyText,
            selected ? palette.AccentText : palette.SecondaryText);
    }

    public static void AssertQualificationContrast()
    {
        foreach (var highContrast in new[] { false, true })
        {
            var palette = Palette(highContrast);
            AssertContrast(palette.PrimaryText, palette.CanvasBackground, 4.5, "primary/canvas");
            AssertContrast(palette.SecondaryText, palette.CanvasBackground, 4.5, "secondary/canvas");
            AssertContrast(palette.BodyText, palette.BoardBackground, 12.0, "body/board");
            AssertContrast(palette.BodyText, palette.CanvasBackground, 12.0, "body/canvas");
            AssertContrast(palette.WarningText, palette.CanvasBackground, 4.5, "warning/canvas");
            AssertContrast(palette.PromptOutline, palette.PromptFill, 3.0, "prompt outline/fill");
            AssertContrast(palette.BodyText, palette.PromptFill, 4.5, "prompt label/fill");
            foreach (var token in PaletteTokens(palette))
            {
                AssertNoMagentaSignal(token, highContrast ? "high-contrast token" : "standard token");
            }

            AssertNoMagentaSignal(MenuBackdrop(highContrast), "menu backdrop");
            AssertNoMagentaSignal(MenuGrid(highContrast), "menu grid");
            AssertNoMagentaSignal(MenuFrame(palette, highContrast), "menu frame");
        }

        var primary = ToOklch(StandardPalette.PrimaryText);
        if (primary.C > 0.10 || primary.L is < 0.84 or > 0.88)
        {
            throw new InvalidOperationException(
                $"Standard primary text was L {primary.L:0.000} C {primary.C:0.000}; required lightness 0.84 to 0.88 and chroma at most 0.10.");
        }

        if (StandardPalette.BodyText.R >= 0.999f
            && StandardPalette.BodyText.G >= 0.999f
            && StandardPalette.BodyText.B >= 0.999f)
        {
            throw new InvalidOperationException("Standard body text must not be pure white.");
        }

        AssertMenuFocusPolicy();
    }

    private static void AssertMenuFocusPolicy()
    {
        foreach (var highContrast in new[] { false, true })
        {
            foreach (var selected in new[] { false, true })
            {
                foreach (var quitsSession in new[] { false, true })
                {
                    var palette = Palette(highContrast);
                    var paint = MenuRow(palette, highContrast, selected, quitsSession);
                    AssertNoMagentaSignal(paint.Key, "menu key");
                    AssertNoMagentaSignal(paint.Label, "menu label");
                    AssertNoMagentaSignal(paint.Rule, "menu rule");
                    if (highContrast)
                    {
                        var mark = selected ? palette.SelectedText : palette.PromptOutline;
                        if (paint.Key != mark
                            || paint.Rule != mark
                            || paint.Label != (selected ? palette.SelectedText : palette.BodyText))
                        {
                            throw new InvalidOperationException(
                                "High-contrast menu focus no longer matches its retained markers.");
                        }

                        continue;
                    }

                    var expectedKey = quitsSession
                        ? palette.WarningText
                        : selected ? palette.AccentText : palette.SecondaryText;
                    if (paint.Key != expectedKey
                        || paint.Label != (selected ? palette.AccentText : palette.BodyText)
                        || paint.Rule != (selected ? palette.AccentText : palette.SecondaryText))
                    {
                        throw new InvalidOperationException(
                            "Standard menu focus must use the cyan accent on the focused row only.");
                    }

                    if (!selected
                        && (paint.Key == palette.AccentText
                            || paint.Label == palette.AccentText
                            || paint.Rule == palette.AccentText))
                    {
                        throw new InvalidOperationException(
                            "An unfocused menu row used the focus accent.");
                    }
                }
            }
        }
    }

    private static IEnumerable<Color> PaletteTokens(ShellPalette palette)
    {
        yield return palette.CanvasBackground;
        yield return palette.BoardBackground;
        yield return palette.PrimaryText;
        yield return palette.SecondaryText;
        yield return palette.BodyText;
        yield return palette.AccentText;
        yield return palette.GoldText;
        yield return palette.MutedGoldText;
        yield return palette.WarningText;
        yield return palette.SelectedText;
        yield return palette.PromptFill;
        yield return palette.PromptOutline;
    }

    private static void AssertNoMagentaSignal(Color color, string name)
    {
        var oklch = ToOklch(color);
        if (oklch.C > 0.08 && oklch.H is >= 300.0 and < 360.0)
        {
            throw new InvalidOperationException(
                $"Shell colour {name} is magenta at hue {oklch.H:0.0} chroma {oklch.C:0.000}.");
        }
    }

    internal readonly record struct Oklch(double L, double C, double H);

    private static Oklch ToOklch(Color color)
    {
        var red = Linearize(color.R);
        var green = Linearize(color.G);
        var blue = Linearize(color.B);
        var longCone = (0.4122214708 * red) + (0.5363325363 * green) + (0.0514459929 * blue);
        var mediumCone = (0.2119034982 * red) + (0.6806995451 * green) + (0.1073969566 * blue);
        var shortCone = (0.0883024619 * red) + (0.2817188376 * green) + (0.6299787005 * blue);
        var longCube = Math.Cbrt(longCone);
        var mediumCube = Math.Cbrt(mediumCone);
        var shortCube = Math.Cbrt(shortCone);
        var lightness = (0.2104542553 * longCube) + (0.7936177850 * mediumCube) - (0.0040720468 * shortCube);
        var greenRed = (1.9779984951 * longCube) - (2.4285922050 * mediumCube) + (0.4505937099 * shortCube);
        var blueYellow = (0.0259040371 * longCube) + (0.7827717662 * mediumCube) - (0.8086757660 * shortCube);
        var hue = Math.Atan2(blueYellow, greenRed) * (180.0 / Math.PI);
        if (hue < 0.0)
        {
            hue += 360.0;
        }

        return new Oklch(lightness, Math.Sqrt((greenRed * greenRed) + (blueYellow * blueYellow)), hue);
    }

    private static Color Rgb(byte red, byte green, byte blue) =>
        new(red / 255f, green / 255f, blue / 255f);

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

internal readonly record struct ShellMenuRowPaint(Color Key, Color Label, Color Rule);

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
