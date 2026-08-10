using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal sealed record ShellPresentationQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    bool CentralizedFontOwner,
    int PaletteCount,
    double StandardPrimaryContrast,
    double StandardSecondaryContrast,
    double HighContrastPrimaryContrast,
    int PromptFamilyCount,
    int GlyphShapeCount,
    bool TextFallbackRetained,
    float MaximumTextScale,
    bool MaximumTextLayoutComplete,
    bool NonColorStateMarkers,
    bool LongCatalogPagination,
    IReadOnlyList<string> VectorBadgeFlows)
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
/// Shared textual markers ensure selection, capture/conflict state, and unlock
/// state never rely on palette color alone.
/// </summary>
internal static class ShellFocusPresentation
{
    public static string SelectionPrefix(bool selected) => selected ? "> " : "  ";

    public static string BindingPrefix(bool selected, bool capture, bool conflict) =>
        !selected ? " " : conflict ? ">!" : capture ? ">?" : ">";

    public static string AchievementMarker(bool unlocked) => unlocked ? "[*]" : "[ ]";

    public static void AssertDistinctMarkers()
    {
        string[] markers =
        [
            SelectionPrefix(selected: true),
            SelectionPrefix(selected: false),
            BindingPrefix(selected: true, capture: false, conflict: false),
            BindingPrefix(selected: true, capture: true, conflict: false),
            BindingPrefix(selected: true, capture: false, conflict: true),
            AchievementMarker(unlocked: true),
            AchievementMarker(unlocked: false),
        ];
        if (markers.Distinct(StringComparer.Ordinal).Count() != markers.Length)
        {
            throw new InvalidOperationException("Shell focus/state text markers are not distinct.");
        }
    }
}
