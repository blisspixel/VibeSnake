using VibeSnake.Persistence;
using System.Text;
using System.Text.Json.Nodes;

namespace VibeSnake.Rules.Tests;

public sealed class PreferencesDocumentTests
{
    [Fact]
    public void Migrates_schema_1_single_volume_to_current_buses_and_controller_defaults()
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
        Assert.Equal(7, result.Document.SchemaVersion);
        Assert.True(result.Document.MasterMuted);
        Assert.Equal(0.5f, result.Document.MasterVolume);
        Assert.Equal(0.5f, result.Document.SfxVolume);
        Assert.True(result.Document.Fullscreen);
        Assert.Equal(0.5f, result.Document.ControllerDeadzone);
        Assert.False(result.Document.MonoOutput);
        Assert.True(result.Document.VibeAdaptationEnabled);
        Assert.False(result.Document.LocalPlaytestSummariesEnabled);
        Assert.Equal(PreferencesDocument.BorderlessMode, result.Document.WindowMode);
        Assert.Equal(PreferencesDocument.HdWindowSize, result.Document.WindowSizePreset);
    }

    [Fact]
    public void Round_trips_current_schema_through_atomic_store()
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
                ControllerDeadzone = 0.65f,
                MonoOutput = true,
                VibeAdaptationEnabled = false,
                LocalPlaytestSummariesEnabled = true,
                WindowMode = PreferencesDocument.ExclusiveFullscreenMode,
                WindowSizePreset = PreferencesDocument.FullHdWindowSize,
            };
            store.Save(document);

            var loaded = store.Load();
            Assert.True(loaded.IsSuccess);
            Assert.NotNull(loaded.Document);
            Assert.Equal(0.25f, loaded.Document.MusicVolume);
            Assert.True(loaded.Document.ReducedMotion);
            Assert.True(loaded.Document.FlashFree);
            Assert.Equal(1.25f, loaded.Document.TextScale);
            Assert.Equal(0.65f, loaded.Document.ControllerDeadzone);
            Assert.True(loaded.Document.MonoOutput);
            Assert.False(loaded.Document.VibeAdaptationEnabled);
            Assert.True(loaded.Document.LocalPlaytestSummariesEnabled);
            Assert.Equal(PreferencesDocument.ExclusiveFullscreenMode, loaded.Document.WindowMode);
            Assert.Equal(PreferencesDocument.FullHdWindowSize, loaded.Document.WindowSizePreset);
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
    public void Injected_write_and_replace_failures_preserve_the_committed_preferences()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-prefs-faults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var baseline = PreferencesDocument.CreateDefaults() with { MusicVolume = 0.25f };
            var physical = new PreferencesStore(root);
            physical.Save(baseline);
            var before = File.ReadAllBytes(physical.PreferencesPath);

            var interrupted = new PreferencesStore(
                root,
                new FaultingPreferencesWriteOperations(failMove: true));
            Assert.Throws<IOException>(() => interrupted.Save(
                baseline with { MusicVolume = 0.75f }));
            Assert.Equal(before, File.ReadAllBytes(physical.PreferencesPath));
            Assert.True(File.Exists(physical.PreferencesPath + ".tmp"));

            physical.Save(baseline with { MusicVolume = 0.5f });
            Assert.False(File.Exists(physical.PreferencesPath + ".tmp"));
            Assert.Equal(0.5f, physical.Load().Document!.MusicVolume);

            var diskFull = new PreferencesStore(
                root,
                new FaultingPreferencesWriteOperations(failWrite: true));
            var committed = File.ReadAllBytes(physical.PreferencesPath);
            Assert.Throws<IOException>(() => diskFull.Save(
                baseline with { MusicVolume = 1.0f }));
            Assert.Equal(committed, File.ReadAllBytes(physical.PreferencesPath));
            Assert.Throws<ArgumentNullException>(() => new PreferencesStore(root, null!));
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

    [Fact]
    public void Migrates_schema_2_with_default_controller_deadzone()
    {
        var schema2 = JsonNode.Parse(
            PreferencesDocument.CreateDefaults().SerializeCanonical())!.AsObject();
        schema2["schemaVersion"] = 2;
        Assert.True(schema2.Remove("controllerDeadzone"));

        var result = PreferencesDocument.Read(schema2.ToJsonString());

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Document!.SchemaVersion);
        Assert.Equal(0.5f, result.Document.ControllerDeadzone);
        Assert.Contains("schema 2", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrates_schema_3_with_mono_output_disabled()
    {
        var schema3 = JsonNode.Parse(
            PreferencesDocument.CreateDefaults().SerializeCanonical())!.AsObject();
        schema3["schemaVersion"] = 3;
        Assert.True(schema3.Remove("monoOutput"));

        var result = PreferencesDocument.Read(schema3.ToJsonString());

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Document!.SchemaVersion);
        Assert.False(result.Document.MonoOutput);
        Assert.Equal(0.5f, result.Document.ControllerDeadzone);
        Assert.Contains("schema 3", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrates_schema_4_with_vibe_adaptation_enabled()
    {
        var schema4 = JsonNode.Parse(
            PreferencesDocument.CreateDefaults().SerializeCanonical())!.AsObject();
        schema4["schemaVersion"] = 4;
        Assert.True(schema4.Remove("vibeAdaptationEnabled"));

        var result = PreferencesDocument.Read(schema4.ToJsonString());

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Document!.SchemaVersion);
        Assert.True(result.Document.VibeAdaptationEnabled);
        Assert.Contains("schema 4", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrates_schema_5_with_local_playtest_collection_disabled()
    {
        var schema5 = JsonNode.Parse(
            PreferencesDocument.CreateDefaults().SerializeCanonical())!.AsObject();
        schema5["schemaVersion"] = 5;
        Assert.True(schema5.Remove("localPlaytestSummariesEnabled"));

        var result = PreferencesDocument.Read(schema5.ToJsonString());

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Document!.SchemaVersion);
        Assert.False(result.Document.LocalPlaytestSummariesEnabled);
        Assert.Contains("schema 5", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_detection_and_schema_1_migration_reject_mistyped_legacy_values()
    {
        Assert.False(
            new PreferencesLoadResult(PreferencesLoadCode.Success, "missing").IsSuccess);
        Assert.True(PreferencesDocument.Read("{}").IsSuccess);

        foreach (var json in new[]
        {
            """{ "schemaVersion": "1" }""",
            """{ "schemaVersion": 0 }""",
            """{ "schemaVersion": 1, "sound_on": "yes" }""",
            """{ "schemaVersion": 1, "soundOn": 1 }""",
            """{ "schemaVersion": 1, "volume": "loud" }""",
            """{ "schemaVersion": 1, "volume": 1e100 }""",
            """{ "schemaVersion": 1, "fullscreen": "yes" }""",
        })
        {
            Assert.False(PreferencesDocument.Read(json).IsSuccess, json);
        }

        var aliases = PreferencesDocument.Read(
            """{ "schemaVersion": 1, "soundOn": false, "volume": 0.25 }""");
        Assert.True(aliases.IsSuccess);
        Assert.True(aliases.Document!.MasterMuted);
        Assert.Equal(0.25f, aliases.Document.MasterVolume);
    }

    [Fact]
    public void Current_schema_rejects_each_mistyped_field_and_defaults_optional_booleans()
    {
        var canonical = JsonNode.Parse(
            PreferencesDocument.CreateDefaults().SerializeCanonical())!.AsObject();
        string[] floatFields =
        [
            "masterVolume", "musicVolume", "sfxVolume", "uiVolume", "textScale",
            "screenShakeIntensity", "controllerDeadzone",
        ];
        foreach (var field in floatFields)
        {
            var missing = (JsonObject)canonical.DeepClone();
            Assert.True(missing.Remove(field));
            Assert.Equal(
                PreferencesLoadCode.InvalidField,
                PreferencesDocument.Read(missing.ToJsonString()).Code);

            var mistyped = (JsonObject)canonical.DeepClone();
            mistyped[field] = "not-a-number";
            Assert.Equal(
                PreferencesLoadCode.InvalidField,
                PreferencesDocument.Read(mistyped.ToJsonString()).Code);
        }

        string[] boolFields =
        [
            "masterMuted", "musicMuted", "sfxMuted", "uiMuted", "fullscreen",
            "reducedMotion", "highContrast", "flashFree", "monoOutput",
            "vibeAdaptationEnabled", "localPlaytestSummariesEnabled",
        ];
        foreach (var field in boolFields)
        {
            var missing = (JsonObject)canonical.DeepClone();
            Assert.True(missing.Remove(field));
            Assert.True(PreferencesDocument.Read(missing.ToJsonString()).IsSuccess);

            var mistyped = (JsonObject)canonical.DeepClone();
            mistyped[field] = 1;
            Assert.Equal(
                PreferencesLoadCode.InvalidField,
                PreferencesDocument.Read(mistyped.ToJsonString()).Code);
        }

        foreach (var field in new[] { "windowMode", "windowSizePreset" })
        {
            var missing = (JsonObject)canonical.DeepClone();
            Assert.True(missing.Remove(field));
            Assert.Equal(
                PreferencesLoadCode.InvalidField,
                PreferencesDocument.Read(missing.ToJsonString()).Code);

            var unsupported = (JsonObject)canonical.DeepClone();
            unsupported[field] = "unsupported";
            Assert.Equal(
                PreferencesLoadCode.InvalidField,
                PreferencesDocument.Read(unsupported.ToJsonString()).Code);
        }
    }

    [Fact]
    public void Migrates_schema_6_fullscreen_to_borderless_and_classic_window_size()
    {
        var schema6 = JsonNode.Parse(
            PreferencesDocument.CreateDefaults().SerializeCanonical())!.AsObject();
        schema6["schemaVersion"] = 6;
        schema6["fullscreen"] = true;
        Assert.True(schema6.Remove("windowMode"));
        Assert.True(schema6.Remove("windowSizePreset"));

        var result = PreferencesDocument.Read(schema6.ToJsonString());

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Document!.SchemaVersion);
        Assert.Equal(PreferencesDocument.BorderlessMode, result.Document.WindowMode);
        Assert.Equal(PreferencesDocument.ClassicWindowSize, result.Document.WindowSizePreset);
        Assert.Contains("schema 6", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Clamping_covers_lower_and_upper_accessibility_and_audio_bounds()
    {
        var clamped = (PreferencesDocument.CreateDefaults() with
        {
            MasterVolume = -1.0f,
            MusicVolume = 2.0f,
            SfxVolume = -2.0f,
            UiVolume = 3.0f,
            TextScale = 0.1f,
            ScreenShakeIntensity = -1.0f,
            ControllerDeadzone = 4.0f,
        }).Clamped();

        Assert.Equal(0.0f, clamped.MasterVolume);
        Assert.Equal(1.0f, clamped.MusicVolume);
        Assert.Equal(0.0f, clamped.SfxVolume);
        Assert.Equal(1.0f, clamped.UiVolume);
        Assert.Equal(0.85f, clamped.TextScale);
        Assert.Equal(0.0f, clamped.ScreenShakeIntensity);
        Assert.Equal(0.9f, clamped.ControllerDeadzone);

        Assert.Equal(
            0.1f,
            (PreferencesDocument.CreateDefaults() with
            {
                ControllerDeadzone = -4.0f,
            }).Clamped().ControllerDeadzone);
    }

    private sealed class FaultingPreferencesWriteOperations(
        bool failWrite = false,
        bool failMove = false) : IPreferencesWriteOperations
    {
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteAllText(string path, string contents, Encoding encoding)
        {
            if (failWrite)
            {
                throw new IOException("Injected storage exhaustion.");
            }

            File.WriteAllText(path, contents, encoding);
        }

        public void Move(string sourcePath, string destinationPath, bool overwrite)
        {
            if (failMove)
            {
                throw new IOException("Injected interrupted replacement.");
            }

            File.Move(sourcePath, destinationPath, overwrite);
        }
    }
}
