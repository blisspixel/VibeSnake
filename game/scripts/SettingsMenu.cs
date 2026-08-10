using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal enum SettingsSection : byte
{
    Gameplay = 0,
    Controls = 1,
    Audio = 2,
    Display = 3,
    Accessibility = 4,
    Data = 5,
}

internal sealed record SettingsItemDefinition(
    string Id,
    string Label,
    string Description);

internal sealed record SettingsScreenQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int PreferenceSchemaVersion,
    int SectionCount,
    int ItemCount,
    bool EveryItemDescribed,
    bool KeyboardRouteComplete,
    bool ControllerRouteComplete,
    bool KeyboardRemappingComplete,
    bool ControllerRemappingComplete,
    bool ConflictSwapAndCancelComplete,
    bool OppositeDeviceBindingsRetained,
    bool SingleActionNavigationComplete,
    bool SectionResetComplete,
    bool FullResetCancelLossless,
    bool FullResetComplete,
    bool SaveReloadComplete,
    bool SaveFailureVisible,
    bool ControllerDeadzoneApplied,
    bool DigitalFallbackRetained,
    bool MonoOutputApplied,
    bool DisplayModesApplied,
    bool VibeAdaptationOptOutApplied,
    bool LocalPlaytestConsentApplied,
    IReadOnlyList<string> Sections)
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
/// Stable player-facing settings information architecture. Main owns current
/// values and actions; this catalog owns section/item identity and descriptions.
/// </summary>
internal static class SettingsMenuCatalog
{
    private static readonly IReadOnlyDictionary<SettingsSection, SettingsItemDefinition[]> Items =
        new Dictionary<SettingsSection, SettingsItemDefinition[]>
        {
            [SettingsSection.Gameplay] =
            [
                new("rules_identity", "Rules identity", "Versioned rules used by scores and replays."),
                new("fixed_step", "Fixed rules step", "Movement advances on deterministic simulation steps."),
                new("input_buffer", "Turn buffer", "Legal turns wait in a bounded exactly-once queue."),
                new(
                    "vibe_adaptation",
                    "Vibe adaptation",
                    "Use the disclosed bounded hunger policy; off uses a separate score category."),
                new(
                    "local_playtest_summaries",
                    "Playtest summaries",
                    "Opt in to bounded run facts stored only on this device; no upload exists."),
            ],
            [SettingsSection.Controls] =
            [
                new(
                    "controller_deadzone",
                    "Stick deadzone",
                    "Tune stick drift rejection from 10 to 90 percent; D-pad stays digital."),
                new("open_bindings", "Edit bindings", "Remap keyboard or controller actions with conflict safety."),
                new("restore_bindings", "Restore bindings", "Restore both device classes to safe defaults."),
            ],
            [SettingsSection.Audio] =
            [
                new("master_volume", "Master volume", "Overall output before individual audio groups."),
                new("master_muted", "Master mute", "Silence every bus while visual cues remain active."),
                new("music_volume", "Music volume", "Music and future radio output level."),
                new("music_muted", "Music mute", "Silence music without hiding critical feedback."),
                new("sfx_volume", "SFX volume", "Gameplay cue output level."),
                new("sfx_muted", "SFX mute", "Silence gameplay cues while captions remain."),
                new("ui_volume", "UI volume", "Menu and navigation cue output level."),
                new("ui_muted", "UI mute", "Silence interface cues only."),
                new(
                    "mono_output",
                    "Mono output",
                    "Downmix the complete Master bus so either speaker carries every cue."),
            ],
            [SettingsSection.Display] =
            [
                new(
                    "window_mode",
                    "Window mode",
                    "Choose windowed, borderless fullscreen, or exclusive fullscreen."),
                new(
                    "window_size",
                    "Window size",
                    "Choose a crisp 4:3, 16:9, or 16:10 window; fullscreen fills the display."),
            ],
            [SettingsSection.Accessibility] =
            [
                new("high_contrast", "High contrast", "Use the qualified high-contrast shell palette."),
                new("reduced_motion", "Reduced motion", "Disable nonessential motion and force shake to zero."),
                new("text_scale", "Text scale", "Scale shell text from 85 to 150 percent."),
                new("screen_shake", "Screen shake", "Set future camera shake from zero to full strength."),
                new("flash_free", "Flash-free", "Remove rapid emphasis while preserving critical text."),
            ],
            [SettingsSection.Data] =
            [
                new("open_diagnostics", "Open diagnostics", "Copy and open the local diagnostics location."),
                new(
                    "reset_tutorial",
                    "Reset tutorial progress",
                    "Offer the first-run tutorial again without removing other player data."),
                new(
                    "reset_preferences",
                    "Reset settings",
                    "Back up, verify, then reset preferences and both binding sets."),
                new(
                    "reset_progression",
                    "Reset progression",
                    "Back up, verify, then reset achievements and tutorial progress."),
                new(
                    "reset_personal_bests",
                    "Reset local scores",
                    "Back up, verify, then reset personal bests and versioned top-ten history."),
                new(
                    "reset_replays",
                    "Reset replays",
                    "Back up, verify, then reset saved replay files."),
                new(
                    "reset_optional_content",
                    "Reset optional content",
                    "Back up, verify, then reset installed and quarantined optional packs."),
                new(
                    "recover_backup",
                    "Recover a backup",
                    "Inspect bounded backups, reject corruption, or restore without overwriting."),
                new(
                    "export_playtest_summaries",
                    "Export summaries",
                    "Write one local JSON export containing only the documented run facts."),
                new(
                    "delete_playtest_summaries",
                    "Delete summaries",
                    "Permanently delete stored summaries and exports after explicit confirmation."),
            ],
        };

    public static IReadOnlyList<SettingsSection> Sections { get; } =
        Enum.GetValues<SettingsSection>();

    public static IReadOnlyList<SettingsItemDefinition> ForSection(SettingsSection section) =>
        Items.TryGetValue(section, out var items)
            ? items
            : throw new ArgumentOutOfRangeException(nameof(section));

    public static int TotalItemCount => Items.Values.Sum(items => items.Length);

    public static void AssertComplete()
    {
        if (Items.Count != Sections.Count || Sections.Count != 6)
        {
            throw new InvalidOperationException("Settings section catalog is incomplete.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in Sections)
        {
            var items = ForSection(section);
            if (items.Count == 0)
            {
                throw new InvalidOperationException(section + " contains no settings rows.");
            }

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.Label)
                    || string.IsNullOrWhiteSpace(item.Description)
                    || !ids.Add(item.Id))
                {
                    throw new InvalidOperationException(
                        section + " contains a blank or duplicate settings row.");
                }
            }
        }
    }
}
