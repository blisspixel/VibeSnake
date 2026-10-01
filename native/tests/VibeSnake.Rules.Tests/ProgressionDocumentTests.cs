using System.Text;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ProgressionDocumentTests
{
    [Fact]
    public void Document_tracks_exact_goals_tour_and_rewards_round_trip()
    {
        var run = new RunAchievementMetrics(
            Score: 600,
            MaxCombo: 6,
            Length: 12,
            FoodEaten: 9,
            WrapCount: 4,
            NearMisses: 3,
            PowerupsCollected: 2,
            SurvivalTicks: 800,
            IsTerminal: true);
        var document = ProgressionDocument.CreateDefaults()
            .WithHumanRun(run, ScoreRunContextCatalog.NormalHuman)
            .WithHighlightedGoal("combo_king");
        foreach (var eventId in new[]
        {
            "local-first-signal",
            "local-wrap-school",
            "local-hold-line",
            "district-power-route",
            "district-combo-carrier",
            "district-noise-test",
            "regional-proof",
            "regional-redline",
        })
        {
            document = document.CompleteTourEvent(eventId);
        }

        document = document
            .WithSelectedCosmeticSet("redline")
            .WithSavedCosmeticSet("redline");
        var read = ProgressionDocument.Read(document.SerializeCanonical());

        Assert.True(read.IsSuccess, read.Message);
        Assert.Equal(document.SerializeCanonical(), read.Document!.SerializeCanonical());
        Assert.Equal(1, read.Document!.Metrics.CompletedHumanRuns);
        Assert.Contains("achievement:high_roller", read.Document.UnlockedRewardIds);
        Assert.Contains("run-card:three-frequencies", read.Document.UnlockedRewardIds);
        Assert.Contains("shed:first-signal", read.Document.UnlockedRewardIds);
        Assert.Contains("loadout-slot:2", read.Document.UnlockedRewardIds);
        Assert.Equal("combo_king", read.Document.HighlightedGoalId);
        Assert.Equal("redline", read.Document.SelectedCosmeticSetId);
        Assert.Contains("redline", read.Document.SavedCosmeticSetIds);
        Assert.Equal(8, read.Document.CompletedTourEventIds.Count);
        Assert.True(read.Document.BuildGoalProgress().Single(item =>
            item.Definition.Id == "century").Completed);
    }

    [Fact]
    public void Tour_requires_prerequisites_and_completion_is_idempotent()
    {
        var document = ProgressionDocument.CreateDefaults();
        Assert.Throws<InvalidOperationException>(() =>
            document.CompleteTourEvent("district-power-route"));
        Assert.Throws<ArgumentException>(() => document.CompleteTourEvent("missing"));
        Assert.Throws<ArgumentException>(() => document.IsCosmeticSetUnlocked("missing"));
        Assert.Throws<InvalidOperationException>(() =>
            document.WithSelectedCosmeticSet("redline"));
        Assert.Throws<InvalidOperationException>(() =>
            document.WithSavedCosmeticSet("redline"));

        var completed = document.CompleteTourEvent("local-first-signal");
        Assert.Equal(completed, completed.CompleteTourEvent("local-first-signal"));
        var selected = completed
            .WithSelectedCosmeticSet("first-signal")
            .WithSavedCosmeticSet("first-signal");
        Assert.Equal(selected, selected.WithSavedCosmeticSet("first-signal"));
        Assert.True(selected.IsCosmeticSetUnlocked("first-signal"));
        Assert.Throws<ArgumentException>(() => completed.WithHighlightedGoal("missing"));
        Assert.Null(completed.WithHighlightedGoal(null).HighlightedGoalId);
    }

    [Fact]
    public void Strict_reader_rejects_unknown_duplicate_future_and_inconsistent_fields()
    {
        var valid = ProgressionDocument.CreateDefaults().SerializeCanonical();
        Assert.Equal(
            ProgressionLoadCode.InvalidField,
            ProgressionDocument.Read(valid.Replace(
                "\"metrics\":",
                "\"unknown\": 1, \"metrics\":",
                StringComparison.Ordinal)).Code);
        Assert.Equal(
            ProgressionLoadCode.InvalidField,
            ProgressionDocument.Read(valid.Replace(
                "\"highlightedGoalId\": null,",
                "\"highlightedGoalId\": null, \"highlightedGoalId\": null,",
                StringComparison.Ordinal)).Code);
        Assert.Equal(
            ProgressionLoadCode.UnsupportedSchema,
            ProgressionDocument.Read(valid.Replace(
                "\"schemaVersion\": 1",
                "\"schemaVersion\": 2",
                StringComparison.Ordinal)).Code);
        Assert.Equal(
            ProgressionLoadCode.InvalidField,
            ProgressionDocument.Read(valid.Replace(
                "\"schemaVersion\": 1,",
                string.Empty,
                StringComparison.Ordinal)).Code);
        Assert.Equal(ProgressionLoadCode.InvalidJson, ProgressionDocument.Read(" ").Code);
        Assert.Equal(
            ProgressionLoadCode.TooLarge,
            ProgressionDocument.Read(new string('x', ProgressionDocument.MaximumDocumentBytes + 1)).Code);

        var unearnedReward = ProgressionDocument.CreateDefaults() with
        {
            UnlockedRewardIds = ["achievement:legend"],
        };
        Assert.Throws<InvalidDataException>(unearnedReward.SerializeCanonical);

        var missingPrerequisites = ProgressionDocument.CreateDefaults() with
        {
            Metrics = new ProgressionMetrics(TourEventsCompleted: 1),
            CompletedTourEventIds = ["district-power-route"],
            UnlockedRewardIds = ["shed:mutagenist"],
        };
        Assert.Throws<InvalidDataException>(missingPrerequisites.SerializeCanonical);

        Assert.Throws<InvalidDataException>(() =>
            (ProgressionDocument.CreateDefaults() with
            {
                Metrics = new ProgressionMetrics(HighestScore: -1),
            }).SerializeCanonical());
        Assert.Throws<InvalidDataException>(() =>
            (ProgressionDocument.CreateDefaults() with
            {
                Metrics = new ProgressionMetrics(HighestScore: 1_000_000_001),
            }).SerializeCanonical());
        Assert.Throws<InvalidDataException>(() =>
            (ProgressionDocument.CreateDefaults() with
            {
                Metrics = new ProgressionMetrics(SavedLoadouts: 6),
            }).SerializeCanonical());
        Assert.Throws<InvalidDataException>(() =>
            (ProgressionDocument.CreateDefaults() with
            {
                CompletedTourEventIds = ["local-first-signal"],
            }).SerializeCanonical());
        Assert.Throws<ArgumentException>(() => new ProgressionStore("relative"));
    }

    [Fact]
    public void Store_defaults_and_atomically_round_trips()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-progression-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProgressionStore(root);
            Assert.True(store.Load().IsSuccess);
            Assert.False(Directory.Exists(root));
            var document = ProgressionDocument.CreateDefaults()
                .WithHighlightedGoal("first_bite");

            store.Save(document);

            Assert.True(File.Exists(store.ProgressionPath));
            Assert.False(File.Exists(store.ProgressionPath + ".tmp"));
            Assert.Equal(
                document.SerializeCanonical(),
                store.Load().Document!.SerializeCanonical());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Cosmetic_selection_and_five_saved_slots_require_earned_sets()
    {
        var document = ProgressionDocument.CreateDefaults();
        foreach (var eventId in new[]
        {
            "local-first-signal",
            "local-wrap-school",
            "local-hold-line",
            "district-power-route",
            "district-combo-carrier",
            "district-noise-test",
            "regional-proof",
            "regional-redline",
            "regional-rim-route",
            "crown-meanline",
            "crown-edge",
        })
        {
            document = document.CompleteTourEvent(eventId);
        }

        foreach (var cosmeticId in new[]
        {
            "first-signal",
            "mutagenist",
            "redline",
            "stillwater",
            "meanline",
        })
        {
            document = document
                .WithSelectedCosmeticSet(cosmeticId)
                .WithSavedCosmeticSet(cosmeticId);
        }

        Assert.Equal(5, document.SavedCosmeticSetIds.Count);
        Assert.True(document.IsCosmeticSetUnlocked("edge-prophet"));
        Assert.Throws<InvalidOperationException>(() =>
            document.WithSavedCosmeticSet("edge-prophet"));
        Assert.True(ProgressionDocument.Read(document.SerializeCanonical()).IsSuccess);

        Assert.Throws<InvalidDataException>(() =>
            (ProgressionDocument.CreateDefaults() with
            {
                SelectedCosmeticSetId = "redline",
            }).SerializeCanonical());
        Assert.Throws<InvalidDataException>(() =>
            (ProgressionDocument.CreateDefaults() with
            {
                Metrics = new ProgressionMetrics(SavedLoadouts: 1),
                SavedCosmeticSetIds = ["classic-signal"],
            }).SerializeCanonical());
        Assert.Throws<InvalidDataException>(() =>
            (ProgressionDocument.CreateDefaults() with
            {
                Metrics = new ProgressionMetrics(CosmeticSetsUnlocked: 1),
            }).SerializeCanonical());
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
        var store = new ProgressionStore(temporary.Path);
        File.WriteAllText(store.ProgressionPath, existing);

        Assert.Throws<InvalidOperationException>(() => store.Save(ProgressionDocument.CreateDefaults()));

        Assert.Equal(existing, File.ReadAllText(store.ProgressionPath));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Store_rechecks_schema_after_load_and_after_staging(bool duringStaging)
    {
        using var temporary = new SaveTestDirectory();
        var physicalStore = new ProgressionStore(temporary.Path);
        physicalStore.Save(ProgressionDocument.CreateDefaults());
        var loaded = physicalStore.Load().Document!;
        const string future = "{\"schema_version\":2,\"newerData\":\"keep\"}";
        var operations = new SaveTestOperations(afterWrite: _ =>
        {
            if (duringStaging)
            {
                File.WriteAllText(physicalStore.ProgressionPath, future);
            }
        });
        var store = new ProgressionStore(temporary.Path, operations);
        if (!duringStaging)
        {
            File.WriteAllText(store.ProgressionPath, future);
        }

        Assert.Throws<InvalidOperationException>(() => store.Save(loaded));

        Assert.Equal(future, File.ReadAllText(store.ProgressionPath));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp-*"));
        Assert.Equal(duringStaging ? 1 : 0, operations.StagePaths.Count);
    }

    [Fact]
    public void Store_interleaved_writers_keep_independent_stages_and_payloads()
    {
        using var temporary = new SaveTestDirectory();
        var first = ProgressionDocument.CreateDefaults();
        var second = ProgressionDocument.CreateDefaults().WithHighlightedGoal("first_bite");
        var secondOperations = new SaveTestOperations();
        var secondStore = new ProgressionStore(temporary.Path, secondOperations);
        var firstOperations = new SaveTestOperations(afterWrite: firstStage =>
        {
            secondStore.Save(second);
            Assert.True(File.Exists(firstStage));
            Assert.Equal(second.SerializeCanonical(), File.ReadAllText(secondStore.ProgressionPath));
        });
        var firstStore = new ProgressionStore(temporary.Path, firstOperations);

        firstStore.Save(first);

        Assert.NotEqual(firstOperations.StagePaths.Single(), secondOperations.StagePaths.Single());
        Assert.Equal(first.SerializeCanonical(), File.ReadAllText(firstStore.ProgressionPath));
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
        var store = new ProgressionStore(temporary.Path);
        store.Save(ProgressionDocument.CreateDefaults());
        var original = File.ReadAllText(store.ProgressionPath);
        var otherStage = store.ProgressionPath + ".tmp-another-writer";
        var legacyStage = store.ProgressionPath + ".tmp";
        File.WriteAllText(otherStage, "other writer");
        File.WriteAllText(legacyStage, "legacy writer");
        var operations = new SaveTestOperations(
            failWrite: failWrite,
            failMove: !failWrite,
            failCleanup: failCleanup);
        var failingStore = new ProgressionStore(temporary.Path, operations);

        var failure = Assert.Throws<IOException>(() => failingStore.Save(ProgressionDocument.CreateDefaults().WithHighlightedGoal("first_bite")));

        Assert.Equal(failWrite ? "stage failure" : "replacement failure", failure.Message);
        Assert.Equal(original, File.ReadAllText(store.ProgressionPath));
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
        var store = new ProgressionStore(temporary.Path);
        File.WriteAllText(store.ProgressionPath, existing);
        var document = ProgressionDocument.CreateDefaults().WithHighlightedGoal("first_bite");

        store.Save(document);

        Assert.Equal(document.SerializeCanonical(), File.ReadAllText(store.ProgressionPath));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp-*"));
    }

    [Fact]
    public void Store_invalid_caller_document_preserves_existing_data()
    {
        using var temporary = new SaveTestDirectory();
        var store = new ProgressionStore(temporary.Path);
        store.Save(ProgressionDocument.CreateDefaults());
        var original = File.ReadAllText(store.ProgressionPath);
        var invalid = ProgressionDocument.CreateDefaults() with { HighlightedGoalId = "unknown" };

        Assert.Throws<InvalidDataException>(() => store.Save(invalid));

        Assert.Equal(original, File.ReadAllText(store.ProgressionPath));
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
                "vibesnake-progression-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

}
