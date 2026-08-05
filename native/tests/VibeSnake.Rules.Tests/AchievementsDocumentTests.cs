using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class AchievementsDocumentTests
{
    [Fact]
    public void Defaults_have_empty_unlock_set()
    {
        var document = AchievementsDocument.CreateDefaults();
        Assert.Equal(1, document.SchemaVersion);
        Assert.Empty(document.UnlockedIds);
        Assert.Empty(document.UnlockedSet);
        Assert.Equal(0, document.UnlockedCount);
        Assert.False(document.IsUnlocked("first_bite"));
        Assert.Throws<ArgumentException>(() => document.IsUnlocked(" "));
    }

    [Fact]
    public void IsUnlocked_reports_merged_ids()
    {
        var document = AchievementsDocument.CreateDefaults()
            .WithUnlocks(["first_bite"]);
        Assert.True(document.IsUnlocked("first_bite"));
        Assert.False(document.IsUnlocked("century"));
    }

    [Fact]
    public void Canonical_serialization_is_stable_and_sorted()
    {
        var document = AchievementsDocument.CreateDefaults()
            .WithUnlocks(["wrap_around", "first_bite", "century"]);
        const string expected =
            """{"schemaVersion":1,"unlockedIds":["century","first_bite","wrap_around"]}""";
        Assert.Equal(expected, document.SerializeCanonical());
        Assert.True(AchievementsDocument.Read(expected).IsSuccess);
    }

    [Fact]
    public void Store_rejects_relative_user_data_root()
    {
        Assert.Throws<ArgumentException>(() => new AchievementsStore("relative/root"));
    }

    [Fact]
    public void Store_rejects_whitespace_user_data_root()
    {
        Assert.Throws<ArgumentException>(() => new AchievementsStore("   "));
    }

    [Fact]
    public void Round_trips_through_atomic_store()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-achievements-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new AchievementsStore(root);
            var document = AchievementsDocument.CreateDefaults()
                .WithUnlocks(["first_bite", "century"]);
            store.Save(document);

            var loaded = store.Load();
            Assert.True(loaded.IsSuccess);
            Assert.NotNull(loaded.Document);
            Assert.Equal(
                new[] { "century", "first_bite" },
                loaded.Document.UnlockedIds);
            Assert.Equal(
                document.SerializeCanonical(),
                loaded.Document.SerializeCanonical());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_loads_defaults()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-achievements-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var loaded = new AchievementsStore(root).Load();
            Assert.True(loaded.IsSuccess);
            Assert.NotNull(loaded.Document);
            Assert.Empty(loaded.Document.UnlockedIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_future_schema_without_document()
    {
        var result = AchievementsDocument.Read(
            """
            {
              "schemaVersion": 99,
              "unlockedIds": []
            }
            """);

        Assert.Equal(AchievementsLoadCode.UnsupportedSchema, result.Code);
        Assert.Null(result.Document);
    }

    [Fact]
    public void Accepts_snake_case_schema_version_alias()
    {
        var result = AchievementsDocument.Read(
            """
            {
              "schema_version": 1,
              "unlocked_ids": ["first_bite"]
            }
            """);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Document);
        Assert.Equal(new[] { "first_bite" }, result.Document.UnlockedIds);
    }

    [Fact]
    public void Rejects_missing_unlocked_ids_array()
    {
        var result = AchievementsDocument.Read(
            """
            {
              "schemaVersion": 1
            }
            """);

        Assert.Equal(AchievementsLoadCode.InvalidField, result.Code);
        Assert.Null(result.Document);
    }

    [Fact]
    public void Empty_payload_returns_empty_code()
    {
        var result = AchievementsDocument.Read("   ");
        Assert.Equal(AchievementsLoadCode.Empty, result.Code);
        Assert.Null(result.Document);
    }

    [Fact]
    public void Invalid_json_returns_invalid_json_code()
    {
        var result = AchievementsDocument.Read("{ not-json");
        Assert.Equal(AchievementsLoadCode.InvalidJson, result.Code);
        Assert.Null(result.Document);
    }

    [Fact]
    public void Rejects_unknown_achievement_ids()
    {
        var result = AchievementsDocument.Read(
            """
            {
              "schemaVersion": 1,
              "unlockedIds": ["first_bite", "not_a_real_achievement"]
            }
            """);

        Assert.Equal(AchievementsLoadCode.InvalidField, result.Code);
        Assert.Null(result.Document);
        Assert.Contains("Unknown achievement id", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithUnlocks_dedupes_sorts_and_rejects_unknown()
    {
        var document = AchievementsDocument.CreateDefaults()
            .WithUnlocks(["first_bite"]);
        var merged = document.WithUnlocks(["century", "first_bite"]);
        Assert.Equal(new[] { "century", "first_bite" }, merged.UnlockedIds);
        Assert.Throws<ArgumentException>(
            () => document.WithUnlocks(["totally_fake"]));
    }

    [Fact]
    public void EvaluateCandidates_skips_already_unlocked_profile_ids()
    {
        var metrics = new RunAchievementMetrics(
            Score: 150,
            MaxCombo: 1,
            Length: 2,
            FoodEaten: 2,
            WrapCount: 0,
            NearMisses: 0,
            PowerupsCollected: 0,
            SurvivalTicks: 10,
            IsTerminal: true);

        var unlocked = AchievementsDocument.CreateDefaults()
            .WithUnlocks(["first_bite"])
            .UnlockedSet;
        var earned = AchievementCatalog.EvaluateCandidates(metrics, unlocked);
        Assert.Contains("century", earned);
        Assert.DoesNotContain("first_bite", earned);
    }

    [Fact]
    public void Rejects_array_root_as_invalid_json_shape()
    {
        var result = AchievementsDocument.Read("[]");
        Assert.Equal(AchievementsLoadCode.InvalidJson, result.Code);
        Assert.Null(result.Document);
    }

    [Fact]
    public void Rejects_null_unlocked_id_entries()
    {
        var result = AchievementsDocument.Read(
            """
            {
              "schemaVersion": 1,
              "unlockedIds": [null]
            }
            """);
        Assert.Equal(AchievementsLoadCode.InvalidField, result.Code);
        Assert.Null(result.Document);
    }

    [Fact]
    public void Rejects_empty_string_unlocked_ids()
    {
        var result = AchievementsDocument.Read(
            """
            {
              "schemaVersion": 1,
              "unlockedIds": [""]
            }
            """);
        Assert.Equal(AchievementsLoadCode.InvalidField, result.Code);
        Assert.Null(result.Document);
    }

    [Fact]
    public void Rejects_whitespace_unlocked_ids()
    {
        var result = AchievementsDocument.Read(
            """
            {
              "schemaVersion": 1,
              "unlockedIds": ["   "]
            }
            """);
        Assert.Equal(AchievementsLoadCode.InvalidField, result.Code);
        Assert.Null(result.Document);
    }

    [Fact]
    public void WithUnlocks_rejects_null_ids()
    {
        var document = AchievementsDocument.CreateDefaults();
        Assert.Throws<ArgumentNullException>(() => document.WithUnlocks(null!));
    }
}
