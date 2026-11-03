using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record PowerSignalColourEvidence(
    string Kind,
    string Hex,
    double Lightness,
    double Chroma,
    double Hue,
    double StandardBoardContrast);

internal sealed record PowerGameplayColourEvidence(
    string Name,
    string Hex,
    double Lightness,
    double Chroma,
    double Hue);

internal sealed record PowerSignalPairEvidence(
    string Left,
    string Right,
    double Normal,
    double Deuteranopia,
    double Protanopia,
    bool IconDistinguishedOnly);

internal sealed record PowerSignalQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int PowerCount,
    int PairCount,
    double MinimumPairDistance,
    int IconDistinguishedPairCount,
    bool NoMagentaGameplaySignal,
    double MinimumBoardContrast,
    double LastStandHeadContrast,
    IReadOnlyList<PowerSignalColourEvidence> Signals,
    IReadOnlyList<PowerGameplayColourEvidence> GameplayColours,
    IReadOnlyList<PowerSignalPairEvidence> Pairs)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

/// <summary>
/// Qualifies the nine power signals, the ghost outline, and the phase body.
/// Letter icons stay the primary channel. A pair below the OKLab floor must be
/// named in the icon-only set or the check fails.
/// </summary>
internal static class PowerSignalQualification
{
    internal const double MinimumPairDistance = 0.08;

    internal const double MinimumSignalContrast = 4.5;

    internal const double MaximumSpectralChroma = 0.12;

    private static readonly HashSet<(PowerKind Left, PowerKind Right)> IconDistinguishedPairs = [];

    public static PowerSignalQualificationEvidence CreateEvidence()
    {
        var kinds = Enum.GetValues<PowerKind>();
        if (kinds.Length != 9)
        {
            throw new InvalidOperationException("Power signal qualification requires the nine power kinds.");
        }

        var standardBoard = VibeLevelDirector.BoardBackground(
            ShellTheme.Palette(highContrast: false).BoardBackground,
            highContrast: false,
            VibeLevel.Grounded);
        var signals = new List<PowerSignalColourEvidence>(kinds.Length);
        var minimumBoardContrast = double.PositiveInfinity;
        foreach (var kind in kinds)
        {
            var color = PowerPresentation.SignalColor(kind);
            AssertNotMagenta(color, kind.ToString());
            if (kind == PowerKind.PhaseShift)
            {
                AssertSpectral(color, kind.ToString());
            }

            var standardContrast = ShellTheme.ContrastRatio(color, standardBoard);
            minimumBoardContrast = Math.Min(
                minimumBoardContrast,
                MinimumBackgroundContrast(color, kind.ToString()));
            if (standardContrast < MinimumSignalContrast)
            {
                throw new InvalidOperationException(
                    $"{kind} contrast on the standard board was {standardContrast:0.00}:1; required {MinimumSignalContrast:0.0}:1.");
            }

            var oklch = ColourMetrics.ToOklch(color);
            signals.Add(new PowerSignalColourEvidence(
                kind.ToString(),
                PowerPresentation.SignalHex(kind),
                oklch.L,
                oklch.C,
                oklch.H,
                standardContrast));
        }

        var phaseHex = PowerPresentation.SignalHex(PowerKind.PhaseShift);
        PowerGameplayColourEvidence[] gameplay =
        [
            GameplayColour("Ghost", PowerPresentation.GhostOutline),
            GameplayColour("PhaseBody", PowerPresentation.PhaseBody),
        ];
        if (gameplay.Any(colour => colour.Hex != phaseHex))
        {
            throw new InvalidOperationException("Ghost and phase body must use the Phase Shift signal.");
        }

        // The Last Stand stroke sits fully inside the head, so board contrast
        // does not show the ring. Phase and Magnet are also inset, but darkening
        // them drops a pair below the OKLab floor.
        var lastStandHeadContrast = ShellTheme.ContrastRatio(
            PowerPresentation.SignalColor(PowerKind.LastStand),
            GameplayPresentation.HeadColor);
        var minimumHeadContrast = VisualHierarchyPolicy.Budget.MinimumGraphicalContrast;
        if (lastStandHeadContrast < minimumHeadContrast)
        {
            throw new InvalidOperationException(
                $"Last Stand contrast on the head was {lastStandHeadContrast:0.00}:1; the ring is drawn inside the head and must stay at least {minimumHeadContrast:0.0}:1.");
        }

        var pairs = new List<PowerSignalPairEvidence>(36);
        for (var leftIndex = 0; leftIndex < kinds.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < kinds.Length; rightIndex++)
            {
                pairs.Add(MeasurePair(kinds[leftIndex], kinds[rightIndex]));
            }
        }

        if (pairs.Count != 36)
        {
            throw new InvalidOperationException("Power signal qualification must list all 36 pairs.");
        }

        var minimumPairDistance = pairs.Min(pair => Math.Min(pair.Normal, Math.Min(pair.Deuteranopia, pair.Protanopia)));
        var iconCount = pairs.Count(pair => pair.IconDistinguishedOnly);
        return new PowerSignalQualificationEvidence(
            SchemaVersion: 1,
            Kind: "power-signal-v1",
            Passed: true,
            PowerCount: kinds.Length,
            PairCount: pairs.Count,
            MinimumPairDistance: minimumPairDistance,
            IconDistinguishedPairCount: iconCount,
            NoMagentaGameplaySignal: true,
            MinimumBoardContrast: minimumBoardContrast,
            LastStandHeadContrast: lastStandHeadContrast,
            Signals: signals,
            GameplayColours: gameplay,
            Pairs: pairs);
    }

    private static PowerGameplayColourEvidence GameplayColour(string name, Color color)
    {
        AssertNotMagenta(color, name);
        AssertSpectral(color, name);
        var oklch = ColourMetrics.ToOklch(color);
        return new PowerGameplayColourEvidence(name, Hex(color), oklch.L, oklch.C, oklch.H);
    }

    private static PowerSignalPairEvidence MeasurePair(PowerKind leftKind, PowerKind rightKind)
    {
        var left = PowerPresentation.SignalColor(leftKind);
        var right = PowerPresentation.SignalColor(rightKind);
        var normal = Distance(left, right, ColourMetrics.ColourVision.Normal);
        var deuteranopia = Distance(left, right, ColourMetrics.ColourVision.Deuteranopia);
        var protanopia = Distance(left, right, ColourMetrics.ColourVision.Protanopia);
        var worst = Math.Min(normal, Math.Min(deuteranopia, protanopia));
        var marked = IsIconDistinguished(leftKind, rightKind);
        if (marked && worst >= MinimumPairDistance)
        {
            throw new InvalidOperationException(
                $"{leftKind} and {rightKind} are marked icon-only but already clear {MinimumPairDistance:0.00} OKLab.");
        }

        if (!marked && worst < MinimumPairDistance)
        {
            var vision = worst == normal
                ? "normal"
                : worst == deuteranopia ? "deuteranopia" : "protanopia";
            throw new InvalidOperationException(
                $"{leftKind} and {rightKind} OKLab distance was {worst:0.0000} under {vision}; required {MinimumPairDistance:0.00} or an icon-only mark.");
        }

        return new PowerSignalPairEvidence(
            leftKind.ToString(),
            rightKind.ToString(),
            normal,
            deuteranopia,
            protanopia,
            marked);
    }

    private static bool IsIconDistinguished(PowerKind left, PowerKind right)
    {
        var ordered = left.CompareTo(right) < 0 ? (left, right) : (right, left);
        return IconDistinguishedPairs.Contains(ordered);
    }

    private static double Distance(Color left, Color right, ColourMetrics.ColourVision vision) =>
        ColourMetrics.Simulate(left, vision).DistanceTo(ColourMetrics.Simulate(right, vision));

    private static double MinimumBackgroundContrast(Color color, string name)
    {
        var minimum = double.PositiveInfinity;
        foreach (var highContrast in new[] { false, true })
        {
            var board = ShellTheme.Palette(highContrast).BoardBackground;
            foreach (var level in Enum.GetValues<VibeLevel>())
            {
                var background = VibeLevelDirector.BoardBackground(board, highContrast, level);
                var ratio = ShellTheme.ContrastRatio(color, background);
                if (ratio < MinimumSignalContrast)
                {
                    var mode = highContrast ? "high-contrast" : "standard";
                    throw new InvalidOperationException(
                        $"{name} contrast on {mode} {level} was {ratio:0.00}:1; required {MinimumSignalContrast:0.0}:1.");
                }

                minimum = Math.Min(minimum, ratio);
            }
        }

        return minimum;
    }

    private static void AssertNotMagenta(Color color, string name)
    {
        if (!ColourMetrics.IsMagenta(color))
        {
            return;
        }

        var oklch = ColourMetrics.ToOklch(color);
        throw new InvalidOperationException(
            $"Gameplay colour {name} is magenta at hue {oklch.H:0.0} chroma {oklch.C:0.000}.");
    }

    private static void AssertSpectral(Color color, string name)
    {
        var oklch = ColourMetrics.ToOklch(color);
        if (oklch.C > MaximumSpectralChroma || oklch.H is < 260.0 or >= 300.0)
        {
            throw new InvalidOperationException(
                $"{name} was L {oklch.L:0.000} C {oklch.C:0.000} H {oklch.H:0.0}; required a blue-violet below hue 300 at chroma {MaximumSpectralChroma:0.00} or less.");
        }
    }

    private static string Hex(Color color)
    {
        var red = Quantize(color.R);
        var green = Quantize(color.G);
        var blue = Quantize(color.B);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static byte Quantize(float channel) =>
        (byte)Math.Clamp((int)Math.Round(channel * 255f, MidpointRounding.AwayFromZero), 0, 255);
}
