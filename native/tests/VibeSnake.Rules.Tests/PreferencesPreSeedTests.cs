using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// The published accessibility pre-seed sample must stay loadable. An agentic
/// playtester with no keyboard injection cannot reach 150 percent text or high
/// contrast through the F6 and F9 hotkeys, so the documented file is their only
/// route and it has to keep parsing against the real schema.
/// </summary>
public sealed class PreferencesPreSeedTests
{
    [Fact]
    public void Canonical_accessibility_profile_is_publishable_and_reloads()
    {
        // The published sample is produced by the real writer, so a documented
        // pre-seed file can never drift from schema 7.
        var profile = PreferencesDocument.CreateDefaults() with
        {
            HighContrast = true,
            ReducedMotion = true,
            TextScale = PreferencesMaximumTextScale,
        };

        var json = profile.SerializeCanonical();
        var result = PreferencesDocument.Read(json);

        Assert.Equal(PreferencesLoadCode.Success, result.Code);
        var reloaded = Assert.IsType<PreferencesDocument>(result.Document);
        Assert.Equal(PreferencesDocument.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.True(reloaded.HighContrast);
        Assert.True(reloaded.ReducedMotion);
        Assert.Equal(PreferencesMaximumTextScale, reloaded.TextScale);
    }

    private const float PreferencesMaximumTextScale = 1.5f;
}
