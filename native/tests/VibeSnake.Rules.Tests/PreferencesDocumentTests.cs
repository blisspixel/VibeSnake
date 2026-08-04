using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class PreferencesDocumentTests
{
    [Fact]
    public void Migrates_schema_1_single_volume_to_schema_2_buses()
    {
        var result = PreferencesDocument.Read(
            """
            {
              "schema_version": 1,
              "sound_on": false,
              "volume": 0.5,
              "fullscreen": true
            }
            """);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Document);
        Assert.Equal(2, result.Document.SchemaVersion);
        Assert.True(result.Document.MasterMuted);
        Assert.Equal(0.5f, result.Document.MasterVolume);
        Assert.Equal(0.5f, result.Document.SfxVolume);
        Assert.True(result.Document.Fullscreen);
    }

    [Fact]
    public void Round_trips_schema_2_through_atomic_store()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new PreferencesStore(root);
            var document = PreferencesDocument.CreateDefaults() with
            {
                MusicVolume = 0.25f,
                ReducedMotion = true,
                FlashFree = true,
                TextScale = 1.25f,
            };
            store.Save(document);

            var loaded = store.Load();
            Assert.True(loaded.IsSuccess);
            Assert.NotNull(loaded.Document);
            Assert.Equal(0.25f, loaded.Document.MusicVolume);
            Assert.True(loaded.Document.ReducedMotion);
            Assert.True(loaded.Document.FlashFree);
            Assert.Equal(1.25f, loaded.Document.TextScale);
            Assert.Equal(
                document.Clamped().SerializeCanonical(),
                loaded.Document.SerializeCanonical());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_future_schema_without_overwrite()
    {
        var result = PreferencesDocument.Read(
            """
            {
              "schemaVersion": 99,
              "masterVolume": 0.5
            }
            """);

        Assert.Equal(PreferencesLoadCode.UnsupportedSchema, result.Code);
        Assert.Null(result.Document);
    }

    [Fact]
    public void Clamps_out_of_range_values_on_serialize()
    {
        var document = PreferencesDocument.CreateDefaults() with
        {
            MasterVolume = 4.0f,
            TextScale = 9.0f,
        };
        var clamped = document.Clamped();
        Assert.Equal(1.0f, clamped.MasterVolume);
        Assert.Equal(1.5f, clamped.TextScale);
    }

    [Fact]
    public void Rejects_empty_and_invalid_json()
    {
        Assert.Equal(PreferencesLoadCode.Empty, PreferencesDocument.Read("").Code);
        Assert.Equal(PreferencesLoadCode.InvalidJson, PreferencesDocument.Read("{").Code);
        Assert.Equal(
            PreferencesLoadCode.InvalidField,
            PreferencesDocument.Read("[]").Code);
    }

    [Fact]
    public void Defaults_load_when_preferences_file_is_absent()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-prefs-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var loaded = new PreferencesStore(root).Load();
            Assert.True(loaded.IsSuccess);
            Assert.Equal(0.8f, loaded.Document!.MasterVolume);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_non_absolute_preferences_root()
    {
        Assert.Throws<ArgumentException>(() => new PreferencesStore("relative/data"));
    }

    [Fact]
    public void Schema_2_rejects_non_finite_fields()
    {
        var result = PreferencesDocument.Read(
            """
            {
              "schemaVersion": 2,
              "masterVolume": "loud",
              "musicVolume": 0.5,
              "sfxVolume": 0.5,
              "uiVolume": 0.5,
              "masterMuted": false,
              "musicMuted": false,
              "sfxMuted": false,
              "uiMuted": false,
              "fullscreen": false,
              "reducedMotion": false,
              "highContrast": false,
              "textScale": 1.0,
              "screenShakeIntensity": 1.0,
              "flashFree": false
            }
            """);
        Assert.Equal(PreferencesLoadCode.InvalidField, result.Code);
    }
}
