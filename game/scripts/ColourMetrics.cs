using Godot;

namespace VibeSnake.Game;

/// <summary>
/// Shared sRGB, OKLab, and colour-vision measurements. Deuteranopia and
/// protanopia use the Machado, Oliveira, and Fernandes 2009 severity-1
/// matrices in linear sRGB.
/// </summary>
internal static class ColourMetrics
{
    private static readonly double[] Deuteranopia =
    [
        0.367322, 0.860646, -0.227968,
        0.280085, 0.672501, 0.047413,
        -0.011820, 0.042940, 0.968881,
    ];

    private static readonly double[] Protanopia =
    [
        0.152286, 1.052583, -0.204868,
        0.114503, 0.786281, 0.099216,
        -0.003882, -0.048116, 1.051998,
    ];

    internal readonly record struct Oklab(double L, double A, double B)
    {
        public double DistanceTo(Oklab other)
        {
            var lightness = L - other.L;
            var greenRed = A - other.A;
            var blueYellow = B - other.B;
            return Math.Sqrt((lightness * lightness) + (greenRed * greenRed) + (blueYellow * blueYellow));
        }
    }

    internal readonly record struct Oklch(double L, double C, double H);

    internal enum ColourVision
    {
        Normal,
        Deuteranopia,
        Protanopia,
    }

    public static double ContrastRatio(Color foreground, Color background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static bool IsMagenta(Color color)
    {
        var oklch = ToOklch(color);
        return oklch.C > 0.08 && oklch.H is >= 300.0 and < 360.0;
    }

    public static Oklch ToOklch(Color color)
    {
        var lab = ToOklab(color);
        var hue = Math.Atan2(lab.B, lab.A) * (180.0 / Math.PI);
        if (hue < 0.0)
        {
            hue += 360.0;
        }

        return new Oklch(lab.L, Math.Sqrt((lab.A * lab.A) + (lab.B * lab.B)), hue);
    }

    public static Oklab ToOklab(Color color) =>
        ToOklabFromLinear(Linearize(color.R), Linearize(color.G), Linearize(color.B));

    public static Oklab Simulate(Color color, ColourVision vision)
    {
        if (vision == ColourVision.Normal)
        {
            return ToOklab(color);
        }

        var red = Linearize(color.R);
        var green = Linearize(color.G);
        var blue = Linearize(color.B);
        var matrix = vision == ColourVision.Deuteranopia ? Deuteranopia : Protanopia;
        return ToOklabFromLinear(
            Clamp(Dot(matrix, 0, red, green, blue)),
            Clamp(Dot(matrix, 3, red, green, blue)),
            Clamp(Dot(matrix, 6, red, green, blue)));
    }

    private static Oklab ToOklabFromLinear(double red, double green, double blue)
    {
        var longCone = (0.4122214708 * red) + (0.5363325363 * green) + (0.0514459929 * blue);
        var mediumCone = (0.2119034982 * red) + (0.6806995451 * green) + (0.1073969566 * blue);
        var shortCone = (0.0883024619 * red) + (0.2817188376 * green) + (0.6299787005 * blue);
        var longCube = Math.Cbrt(longCone);
        var mediumCube = Math.Cbrt(mediumCone);
        var shortCube = Math.Cbrt(shortCone);
        var lightness = (0.2104542553 * longCube) + (0.7936177850 * mediumCube) - (0.0040720468 * shortCube);
        var greenRed = (1.9779984951 * longCube) - (2.4285922050 * mediumCube) + (0.4505937099 * shortCube);
        var blueYellow = (0.0259040371 * longCube) + (0.7827717662 * mediumCube) - (0.8086757660 * shortCube);
        return new Oklab(lightness, greenRed, blueYellow);
    }

    private static double Dot(double[] matrix, int offset, double red, double green, double blue) =>
        (matrix[offset] * red) + (matrix[offset + 1] * green) + (matrix[offset + 2] * blue);

    private static double Clamp(double channel) => Math.Clamp(channel, 0.0, 1.0);

    private static double RelativeLuminance(Color color) =>
        (0.2126 * Linearize(color.R))
        + (0.7152 * Linearize(color.G))
        + (0.0722 * Linearize(color.B));

    private static double Linearize(float channel) =>
        channel <= 0.04045f
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
