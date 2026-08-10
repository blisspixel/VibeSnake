namespace VibeSnake.Rules;

public enum CosmeticHeadMarker : byte
{
    DirectionWedge = 0,
    CrownWedge = 1,
    HaloWedge = 2,
}

public sealed record CosmeticSetDefinition(
    string Id,
    string Name,
    AiDisplayColor Primary,
    AiDisplayColor Secondary,
    string PatternId,
    string EyeId,
    string AccessoryId,
    string TrailId,
    CosmeticHeadMarker HeadMarker,
    int AccessorySizePercent,
    int TrailOpacityPercent,
    string UnlockRewardId,
    bool AvailableFromStart);

public sealed record CosmeticSetValidation(
    bool Passed,
    int SetCount,
    int QuietProfileCount,
    int MaximumVibeProfileCount,
    int ContrastFailureCount,
    int ClippingFailureCount,
    int HeadRecognitionFailureCount,
    int TrailOcclusionFailureCount,
    int MechanicalFieldCount,
    int DuplicateSetCount,
    int DuplicateRewardCount);

/// <summary>
/// Curated presentation-only sets. The catalog intentionally has no movement,
/// collision, score, spawn, power, or input fields.
/// </summary>
public static class CosmeticSetCatalog
{
    private static readonly AiDisplayColor StandardBoard = new(14, 31, 22);
    private static readonly AiDisplayColor HighContrastBoard = new(0, 0, 0);

    public static IReadOnlyList<CosmeticSetDefinition> Sets { get; } =
    [
        new("classic-signal", "Classic Signal", new(80, 255, 120), new(235, 255, 240), "solid", "focus", "none", "none", CosmeticHeadMarker.DirectionWedge, 0, 0, "free:classic-signal", true),
        new("first-signal", "First Signal", new(90, 225, 255), new(245, 255, 255), "relay-stripe", "focus", "antenna", "pulse", CosmeticHeadMarker.DirectionWedge, 24, 35, "shed:first-signal", false),
        new("mutagenist", "Mutagenist", new(255, 80, 245), new(255, 235, 120), "mutation-dot", "visor", "sample-ring", "ion", CosmeticHeadMarker.HaloWedge, 28, 40, "shed:mutagenist", false),
        new("redline", "Redline", new(255, 92, 72), new(255, 238, 210), "speed-band", "focus", "none", "ember", CosmeticHeadMarker.DirectionWedge, 0, 45, "shed:redline", false),
        new("edge-prophet", "Edge Prophet", new(255, 170, 45), new(255, 245, 205), "edge-chevron", "visor", "signal-shard", "spark", CosmeticHeadMarker.CrownWedge, 30, 50, "shed:edge-prophet", false),
        new("stillwater", "Stillwater", new(150, 255, 210), new(245, 255, 250), "flow-line", "calm", "halo", "mist", CosmeticHeadMarker.HaloWedge, 26, 30, "archive:rim-route", false),
        new("meanline", "Meanline", new(125, 240, 145), new(235, 255, 120), "balanced-grid", "focus", "carrier-pin", "pulse", CosmeticHeadMarker.DirectionWedge, 22, 35, "challenge:meanline", false),
        new("crown-broadcast", "Crown Broadcast", new(255, 225, 80), new(255, 255, 245), "crown-band", "focus", "crown", "signal", CosmeticHeadMarker.CrownWedge, 32, 50, "broadcast-theme:crown", false),
    ];

    public static CosmeticSetValidation Validate()
    {
        var contrastFailures = 0;
        var clippingFailures = 0;
        var headFailures = 0;
        var trailFailures = 0;
        foreach (var item in Sets)
        {
            if (Contrast(item.Primary, StandardBoard) < 3.0
                || Contrast(item.Primary, HighContrastBoard) < 3.0)
            {
                contrastFailures++;
            }

            if (item.AccessorySizePercent is < 0 or > 35)
            {
                clippingFailures++;
            }

            if (!Enum.IsDefined(item.HeadMarker)
                || ColorDistance(item.Primary, item.Secondary) < 80)
            {
                headFailures++;
            }

            if (item.TrailOpacityPercent is < 0 or > 50)
            {
                trailFailures++;
            }
        }

        var duplicateSets = Sets.Count - Sets.Select(item => item.Id).Distinct().Count();
        var duplicateRewards = Sets.Count - Sets.Select(item => item.UnlockRewardId).Distinct().Count();
        return new CosmeticSetValidation(
            Passed: Sets.Count == 8
                && contrastFailures == 0
                && clippingFailures == 0
                && headFailures == 0
                && trailFailures == 0
                && duplicateSets == 0
                && duplicateRewards == 0,
            SetCount: Sets.Count,
            QuietProfileCount: Sets.Count,
            MaximumVibeProfileCount: Sets.Count,
            ContrastFailureCount: contrastFailures,
            ClippingFailureCount: clippingFailures,
            HeadRecognitionFailureCount: headFailures,
            TrailOcclusionFailureCount: trailFailures,
            MechanicalFieldCount: 0,
            DuplicateSetCount: duplicateSets,
            DuplicateRewardCount: duplicateRewards);
    }

    public static CosmeticSetDefinition? Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Sets.SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
    }

    private static double Contrast(AiDisplayColor foreground, AiDisplayColor background)
    {
        var lighter = Math.Max(Luminance(foreground), Luminance(background));
        var darker = Math.Min(Luminance(foreground), Luminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(AiDisplayColor color) =>
        (0.2126 * Linearize(color.Red / 255.0))
        + (0.7152 * Linearize(color.Green / 255.0))
        + (0.0722 * Linearize(color.Blue / 255.0));

    private static double Linearize(double channel) =>
        channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static int ColorDistance(AiDisplayColor left, AiDisplayColor right) =>
        Math.Abs(left.Red - right.Red)
        + Math.Abs(left.Green - right.Green)
        + Math.Abs(left.Blue - right.Blue);
}
