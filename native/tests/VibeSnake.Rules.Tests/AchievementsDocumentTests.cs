using System.Text;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class AchievementsDocumentTests
{
    private static readonly string[] CenturyAndFirstBite = ["century", "first_bite"];
    private static readonly string[] FirstBite = ["first_bite"];

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
                CenturyAndFirstBite,
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
        Assert.Equal(FirstBite, result.Document.UnlockedIds);
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
        Assert.Equal(CenturyAndFirstBite, merged.UnlockedIds);
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

    [Fact]
    public void Rejects_numeric_unlocked_ids_container()
    {
        var result = AchievementsDocument.Read(
            """
            {
              "schemaVersion": 1,
              "unlockedIds": 3
            }
            """);
        Assert.Equal(AchievementsLoadCode.InvalidField, result.Code);
        Assert.Null(result.Document);
    }

    [Fact]
    public void WithUnlocks_empty_sequence_preserves_existing_ids()
    {
        var document = AchievementsDocument.CreateDefaults()
            .WithUnlocks(["first_bite"]);
        var same = document.WithUnlocks(Array.Empty<string>());
        Assert.Equal(document.UnlockedIds, same.UnlockedIds);
        Assert.Equal(document.SerializeCanonical(), same.SerializeCanonical());
    }

    [Fact]
    public void Current_schema_version_is_one()
    {
        Assert.Equal(1, AchievementsDocument.CurrentSchemaVersion);
        Assert.Equal("achievements.json", AchievementsDocument.FileName);
    }

    [Fact]
    public void Maximum_unlock_count_is_bounded()
    {
        Assert.Equal(256, AchievementsDocument.MaximumUnlockCount);
        Assert.True(AchievementCatalog.Definitions.Count <= AchievementsDocument.MaximumUnlockCount);
    }
    [Theory]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schema_version\":2}")]
    [InlineData("{\"schemaVersion\":1,\"schema_version\":2}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schema_version\":1,\"schema_version\":1}")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    [InlineData("{\"schemaVersion\":null}")]
    [InlineData("{\"schemaVersion\":0}")]
    [InlineData("{\"schemaVersion\":1.5}")]
    [InlineData("{\"schemaVersion\":2147483648}")]
    public void Store_preserves_unsupported_or_ambiguous_existing_schema(string existing)
    {
        using var temporary = new SaveTestDirectory();
        var store = new AchievementsStore(temporary.Path);
        File.WriteAllText(store.AchievementsPath, existing);

        Assert.Throws<InvalidOperationException>(() => store.Save(AchievementsDocument.CreateDefaults()));

        Assert.Equal(existing, File.ReadAllText(store.AchievementsPath));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Store_rechecks_schema_after_load_and_after_staging(bool duringStaging)
    {
        using var temporary = new SaveTestDirectory();
        var physicalStore = new AchievementsStore(temporary.Path);
        physicalStore.Save(AchievementsDocument.CreateDefaults());
        var loaded = physicalStore.Load().Document!;
        const string future = "{\"schema_version\":2,\"newerData\":\"keep\"}";
        var operations = new SaveTestOperations(afterWrite: _ =>
        {
            if (duringStaging)
            {
                File.WriteAllText(physicalStore.AchievementsPath, future);
            }
        });
        var store = new AchievementsStore(temporary.Path, operations);
        if (!duringStaging)
        {
            File.WriteAllText(store.AchievementsPath, future);
        }

        Assert.Throws<InvalidOperationException>(() => store.Save(loaded));

        Assert.Equal(future, File.ReadAllText(store.AchievementsPath));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp-*"));
        Assert.Equal(duringStaging ? 1 : 0, operations.StagePaths.Count);
    }

    [Fact]
    public void Store_interleaved_writers_keep_independent_stages_and_payloads()
    {
        using var temporary = new SaveTestDirectory();
        var first = AchievementsDocument.CreateDefaults();
        var second = AchievementsDocument.CreateDefaults().WithUnlocks(["first_bite"]);
        var secondOperations = new SaveTestOperations();
        var secondStore = new AchievementsStore(temporary.Path, secondOperations);
        var firstOperations = new SaveTestOperations(afterWrite: firstStage =>
        {
            secondStore.Save(second);
            Assert.True(File.Exists(firstStage));
            Assert.Equal(second.SerializeCanonical(), File.ReadAllText(secondStore.AchievementsPath));
        });
        var firstStore = new AchievementsStore(temporary.Path, firstOperations);

        firstStore.Save(first);

        Assert.NotEqual(firstOperations.StagePaths.Single(), secondOperations.StagePaths.Single());
        Assert.Equal(first.SerializeCanonical(), File.ReadAllText(firstStore.AchievementsPath));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp-*"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Store_failed_save_preserves_committed_data_and_other_stages(
        bool failWrite,
        bool failCleanup)
    {
        using var temporary = new SaveTestDirectory();
        var store = new AchievementsStore(temporary.Path);
        store.Save(AchievementsDocument.CreateDefaults());
        var original = File.ReadAllText(store.AchievementsPath);
        var otherStage = store.AchievementsPath + ".tmp-another-writer";
        var legacyStage = store.AchievementsPath + ".tmp";
        File.WriteAllText(otherStage, "other writer");
        File.WriteAllText(legacyStage, "legacy writer");
        var operations = new SaveTestOperations(
            failWrite: failWrite,
            failMove: !failWrite,
            failCleanup: failCleanup);
        var failingStore = new AchievementsStore(temporary.Path, operations);

        var failure = Assert.Throws<IOException>(() => failingStore.Save(AchievementsDocument.CreateDefaults().WithUnlocks(["first_bite"])));

        Assert.Equal(failWrite ? "stage failure" : "replacement failure", failure.Message);
        Assert.Equal(original, File.ReadAllText(store.AchievementsPath));
        Assert.Equal("other writer", File.ReadAllText(otherStage));
        Assert.Equal("legacy writer", File.ReadAllText(legacyStage));
        Assert.Equal(failCleanup, File.Exists(operations.StagePaths.Single()));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{\"schemaVersion\":1,\"schema_version\":1}")]
    public void Store_explicit_recovery_allows_malformed_json_and_matching_supported_aliases(string existing)
    {
        using var temporary = new SaveTestDirectory();
        var store = new AchievementsStore(temporary.Path);
        File.WriteAllText(store.AchievementsPath, existing);
        var document = AchievementsDocument.CreateDefaults().WithUnlocks(["first_bite"]);

        store.Save(document);

        Assert.Equal(document.SerializeCanonical(), File.ReadAllText(store.AchievementsPath));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp-*"));
    }

    [Fact]
    public void Store_invalid_caller_document_preserves_existing_data()
    {
        using var temporary = new SaveTestDirectory();
        var store = new AchievementsStore(temporary.Path);
        store.Save(AchievementsDocument.CreateDefaults());
        var original = File.ReadAllText(store.AchievementsPath);
        var invalid = AchievementsDocument.CreateDefaults() with { UnlockedIds = ["unknown"] };

        Assert.Throws<InvalidDataException>(() => store.Save(invalid));

        Assert.Equal(original, File.ReadAllText(store.AchievementsPath));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp-*"));
    }

    private sealed class SaveTestOperations(
        Action<string>? afterWrite = null,
        bool failWrite = false,
        bool failMove = false,
        bool failCleanup = false) : IPreferencesWriteOperations
    {
        public List<string> StagePaths { get; } = [];

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteAllText(string path, string contents, Encoding encoding)
        {
            StagePaths.Add(path);
            if (failWrite)
            {
                File.WriteAllText(path, "partial");
                throw new IOException("stage failure");
            }

            PhysicalPreferencesWriteOperations.Instance.WriteAllText(path, contents, encoding);
            afterWrite?.Invoke(path);
        }

        public void Move(string source, string destination, bool overwrite)
        {
            if (failMove)
            {
                throw new IOException("replacement failure");
            }

            File.Move(source, destination, overwrite);
        }

        public void Delete(string path)
        {
            if (failCleanup)
            {
                throw new UnauthorizedAccessException("cleanup failure");
            }

            File.Delete(path);
        }
    }

    private sealed class SaveTestDirectory : IDisposable
    {
        public SaveTestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "vibesnake-achievements-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

}
