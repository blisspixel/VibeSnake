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
}
